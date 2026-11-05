using System.Net.Sockets;
using System.Text;
using Menage.Core.Armazenamento;
using Menage.Core.Http;
using Menage.Core.Nexus;
using Menage.Core.Processos;
using Microsoft.Extensions.DependencyInjection;

namespace Menage.Core.Tomcat;

/// <summary>Lista de ambientes (<c>tomcat-express.json</c>).</summary>
public sealed class RepositorioDoTomcat(IArmazenamento<ConfiguracaoDoTomcat> armazenamento)
{
    ConfiguracaoDoTomcat? _cache;

    public ConfiguracaoDoTomcat Configuracao => _cache ??= armazenamento.Carregar().ValorOu(ConfiguracaoDoTomcat.Padrao());

    public Resultado<Nada> Salvar(ConfiguracaoDoTomcat nova)
    {
        var r = armazenamento.Salvar(nova);
        if (r.EhOk) _cache = nova;
        return r;
    }
}

/// <summary>Sobe, para e acompanha o Tomcat de UM ambiente. Reencontra o processo pelo PID se o Menage foi reaberto.</summary>
public sealed class ExecucaoDoTomcat
{
    const int LimiteDoLog = 8000;
    readonly ILancadorDeProcessos _lancador;
    readonly IPortasDoSistema _portas;
    readonly IClienteHttp _http;
    readonly List<string> _log = [];
    readonly object _trava = new();
    IProcessoLongo? _processo;
    bool _dono;
    bool _parando;
    long _descartadas;
    DateTime _ultimaSaude = DateTime.MinValue;
    string? _arquivoCauda;
    long _posCauda;

    public ExecucaoDoTomcat(AmbienteTomcat ambiente, ILancadorDeProcessos lancador, IPortasDoSistema portas, IClienteHttp http)
    {
        Ambiente = ambiente;
        _lancador = lancador;
        _portas = portas;
        _http = http;
        if ((_processo = lancador.Reencontrar(ambiente.Pid, ambiente.PidInicioTicks)) is not null) Interpretador.MarcarSubiu();
    }

    public AmbienteTomcat Ambiente { get; set; }

    public InterpretadorDeLog Interpretador { get; } = new();

    /// <summary>Avisa a tela quando o PID mudou, para ela gravar o ambiente.</summary>
    public event Action<AmbienteTomcat>? AmbienteMudou;

    public bool Vivo => _processo?.Vivo ?? false;

    public long TotalDeLinhas
    {
        get
        {
            lock (_trava) return _descartadas + _log.Count;
        }
    }

    public IReadOnlyList<string> LinhasDesde(long indice, out long proximo)
    {
        lock (_trava)
        {
            proximo = _descartadas + _log.Count;
            return [.. _log.Skip((int)Math.Max(0, indice - _descartadas))];
        }
    }

    public SituacaoDoTomcat Situacao()
    {
        if (Vivo) return _parando ? SituacaoDoTomcat.Parando : TudoNoAr() ? SituacaoDoTomcat.NoAr : SituacaoDoTomcat.Subindo;
        if (_processo is not null)
        {
            _processo = null;
            if (Ambiente.Pid != 0) Atualizar(Ambiente with { Pid = 0, PidInicioTicks = 0 });
        }

        _parando = false;
        return _portas.Escutando(Ambiente.Porta) ? SituacaoDoTomcat.PortaOcupada : SituacaoDoTomcat.Parado;
    }

    public IEnumerable<string> ContextosPresentes()
    {
        var webapps = Path.Combine(Ambiente.Pasta, "webapps");
        foreach (var c in new[] { AmbienteTomcat.ContextoVendas, AmbienteTomcat.ContextoDados })
            if (File.Exists(Path.Combine(webapps, c + ".war")) || Directory.Exists(Path.Combine(webapps, c))) yield return c;
    }

    public string ResumoDasApps()
    {
        var apps = Interpretador.Apps;
        return string.Join("   ", ContextosPresentes().Select(c => $"{c}: {(apps.TryGetValue(c, out var s) ? s : "aguardando")}"));
    }

    public async Task<Resultado<Nada>> SubirAsync(ISet<int> reservadas, PreparadorDoTomcat preparador, CancellationToken cancelar = default)
    {
        if (PastaDoTomcat.Validar(Ambiente.Pasta) is { } invalida) return Resultado.Falha<Nada>(invalida);
        var situacao = Situacao();
        if (situacao is SituacaoDoTomcat.Subindo or SituacaoDoTomcat.NoAr) return Resultado.Falha<Nada>("O ambiente já está em execução", null, "Use Parar antes de subir de novo.");
        if (situacao == SituacaoDoTomcat.Parando) return Resultado.Falha<Nada>("O ambiente está parando", null, "Espere terminar e suba de novo.");
        if (PastaDoTomcat.ValidarJava(Ambiente.Pasta) is { } semJava) return Resultado.Falha<Nada>(semJava);
        var ruins = PastaDoTomcat.LinhasInvalidasDoBat(Ambiente.Pasta);
        if (ruins.Count > 0)
            return Resultado.Falha<Nada>("O wmw_database_config.bat tem linha inválida", $"{string.Join("; ", ruins)} (o Windows tentaria executar isso como comando e o Tomcat sairia com código 255)",
                @"Abra bin\wmw_database_config.bat e corrija ou apague essa linha. Costuma ser texto colado antes do @echo off.");
        if (!ContextosPresentes().Any()) return Resultado.Falha<Nada>("Não há aplicações para subir", "webapps não tem wmwvendas nem wmwdados.", "Clique em Preparar ambiente para baixar os .war do Nexus.");

        var ajuste = preparador.SincronizarPortas(Ambiente, reservadas, Registrar);
        if (!ajuste.EhOk) return ajuste;
        var p = ServerXml.Ler(ArquivoDeTexto.Ler(Path.Combine(Ambiente.Pasta, "conf", "server.xml")).Texto);
        foreach (var porta in new[] { Ambiente.Porta, p.Parada, p.Ajp }.Where(x => x > 0).Distinct())
        {
            if (_portas.Livre(porta)) continue;
            var quem = await _portas.OcupanteAsync(porta, cancelar);
            return Resultado.Falha<Nada>($"A porta {porta} já está em uso{(quem.Length > 0 ? " por " + quem : "")}",
                porta == Ambiente.Porta ? "Outro programa (ou outro Tomcat) escuta na porta HTTP deste ambiente." : $"A porta {porta} é a de {(porta == p.Parada ? "parada" : "AJP")} deste Tomcat.",
                porta == Ambiente.Porta ? "Pare esse programa ou escolha outra porta para o ambiente." : "Pare o programa que a usa e tente de novo.");
        }

        Interpretador.Reiniciar();
        _parando = false;
        lock (_trava)
        {
            _descartadas += _log.Count;
            _log.Clear();
        }

        var java = PastaDoTomcat.JavaHome(Ambiente.Pasta)!;
        Registrar($"[Menage] Subindo {Ambiente.Titulo} na porta {Ambiente.Porta} ({PastaDoTomcat.VersaoJava(java)}, {PastaDoTomcat.Versao(Ambiente.Pasta)})");
        var bat = Path.Combine(Ambiente.Pasta, "bin", "catalina.bat");
        var comando = new ComandoProcesso("cmd.exe", ["/d", "/s", "/c", $"\"\"{bat}\" run\""], Ambiente.Pasta, new Dictionary<string, string?>
        {
            ["CATALINA_HOME"] = Ambiente.Pasta,
            ["CATALINA_BASE"] = Ambiente.Pasta,
            ["JAVA_HOME"] = java,
            ["CATALINA_OPTS"] = null,
            ["CATALINA_TMPDIR"] = null,
        });
        var inicio = _lancador.Iniciar(comando, l => Linha(InterpretadorDeLog.Decodificar(l)), Saiu);
        if (!inicio.EhOk) return Resultado.Falha<Nada>(new Erro("Não consegui iniciar o Tomcat", inicio.Erro.Explicacao, $"Confira se a pasta {Ambiente.Pasta} está acessível e se o cmd.exe roda."));
        _processo = inicio.Valor;
        _dono = true;
        Atualizar(Ambiente with { Pid = _processo.Id, PidInicioTicks = _processo.InicioTicks });
        return Resultado.Ok();
    }

    /// <summary>Pergunta ao próprio Tomcat se cada aplicação responde (a tela chama de tempos em tempos, fora da thread de tela).</summary>
    public async Task VerificarSaudeAsync(CancellationToken cancelar = default)
    {
        if (!Vivo || (DateTime.Now - _ultimaSaude).TotalSeconds < 2) return;
        _ultimaSaude = DateTime.Now;
        foreach (var c in ContextosPresentes())
        {
            if (Interpretador.Apps.TryGetValue(c, out var atual) && atual == "falhou") continue;
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelar);
            limite.CancelAfter(TimeSpan.FromSeconds(4));
            var r = await _http.ObterTextoAsync(new Uri($"http://127.0.0.1:{Ambiente.Porta}/{c}/"), cancelar: limite.Token);
            Interpretador.DefinirApp(c, r.EhOk ? Classificar(r.Valor.Status) : limite.IsCancellationRequested ? "respondendo devagar" : "aguardando");
        }
    }

    public static string Classificar(int codigo) => codigo switch
    {
        >= 200 and < 400 or 401 or 403 => "ok",
        404 => "aguardando",
        503 => "iniciando",
        _ => $"HTTP {codigo}",
    };

    /// <summary>Quando o Menage não tem a saída do processo (foi reaberto), lê o fim do catalina.*.log.</summary>
    public void AtualizarCauda()
    {
        if (_dono || !Vivo) return;
        try
        {
            var pasta = Path.Combine(Ambiente.Pasta, "logs");
            if (!Directory.Exists(pasta)) return;
            var arquivo = Directory.GetFiles(pasta, "catalina.*.log").OrderByDescending(f => new FileInfo(f).LastWriteTime).FirstOrDefault();
            if (arquivo is null) return;
            using var fs = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (arquivo != _arquivoCauda)
            {
                _arquivoCauda = arquivo;
                _posCauda = Math.Max(0, fs.Length - 60000);
            }

            if (fs.Length < _posCauda) _posCauda = 0;
            fs.Position = _posCauda;
            using var leitor = new StreamReader(fs, Encoding.UTF8);
            while (leitor.ReadLine() is { } l) Linha(l);
            _posCauda = fs.Position;
        }
        catch (IOException) { /* o log está em uso ou sumiu: tenta no próximo ciclo */ }
    }

    /// <summary>SHUTDOWN pela porta de parada; se o java não sair no prazo, encerra a árvore do processo.</summary>
    public async Task<string> PararAsync(TimeSpan espera, CancellationToken cancelar = default)
    {
        if (!Vivo) return "O ambiente já estava parado.";
        _parando = true;
        var resultado = "Parado.";
        var enviou = false;
        try
        {
            var p = ServerXml.Ler(ArquivoDeTexto.Ler(Path.Combine(Ambiente.Pasta, "conf", "server.xml")).Texto);
            if (p.Parada > 0)
            {
                using var cliente = new TcpClient();
                await cliente.ConnectAsync("127.0.0.1", p.Parada, cancelar);
                await cliente.GetStream().WriteAsync(Encoding.ASCII.GetBytes(p.ComandoDeParada), cancelar);
                enviou = true;
                Registrar($"[Menage] Pedido de parada enviado (porta {p.Parada}).");
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException)
        {
            Registrar("[Menage] Não consegui falar com a porta de parada: " + ex.Message);
        }

        var limite = DateTime.Now + espera;
        while (Vivo && DateTime.Now < limite && !cancelar.IsCancellationRequested) await Task.Delay(200, CancellationToken.None);
        if (Vivo)
        {
            Registrar($"[Menage] O Tomcat não encerrou em {espera.TotalSeconds:0} s{(enviou ? "" : " (parada gentil não funcionou)")}; encerrando o processo à força.");
            await _processo!.EncerrarArvoreAsync();
            resultado = $"Parado à força (o Tomcat não saiu sozinho em {espera.TotalSeconds:0} s).";
        }

        _parando = false;
        _processo = null;
        Interpretador.Reiniciar();
        Atualizar(Ambiente with { Pid = 0, PidInicioTicks = 0 });
        return resultado;
    }

    bool TudoNoAr()
    {
        if (!Interpretador.ServidorSubiu) return false;
        var apps = Interpretador.Apps;
        var presentes = ContextosPresentes().ToList();
        return presentes.Count > 0 && presentes.All(c => apps.TryGetValue(c, out var s) && s == "ok");
    }

    void Linha(string linha)
    {
        Registrar(linha);
        Interpretador.Linha(linha);
    }

    void Saiu(int codigo)
    {
        Registrar($"[Menage] O processo do Tomcat terminou (código {codigo}).");
        Interpretador.Saiu(codigo, _parando);
    }

    void Registrar(string linha)
    {
        lock (_trava)
        {
            _log.Add(linha);
            if (_log.Count <= LimiteDoLog) return;
            _log.RemoveRange(0, 2000);
            _descartadas += 2000;
        }
    }

    void Atualizar(AmbienteTomcat novo)
    {
        Ambiente = novo;
        AmbienteMudou?.Invoke(novo);
    }
}

/// <summary>Uma <see cref="ExecucaoDoTomcat"/> por ambiente, para a janela reencontrar o que já está rodando.</summary>
public sealed class GerenteDeTomcat(ILancadorDeProcessos lancador, IPortasDoSistema portas, IClienteHttp http)
{
    readonly Dictionary<string, ExecucaoDoTomcat> _mapa = [];

    public ExecucaoDoTomcat De(AmbienteTomcat ambiente)
    {
        lock (_mapa)
        {
            if (!_mapa.TryGetValue(ambiente.Id, out var e)) _mapa[ambiente.Id] = e = new ExecucaoDoTomcat(ambiente, lancador, portas, http);
            else e.Ambiente = ambiente;
            return e;
        }
    }

    public void Esquecer(string id)
    {
        lock (_mapa) _mapa.Remove(id);
    }
}

public static class TomcatExtensions
{
    public static IServiceCollection AddTomcat(this IServiceCollection servicos) =>
        servicos
            .AddSingleton<IArmazenamento<ConfiguracaoDoTomcat>>(sp =>
                new Armazenamento<ConfiguracaoDoTomcat>(sp.GetRequiredService<PastaDeDados>(), ConfiguracaoDoTomcat.NomeDoArquivo, padrao: ConfiguracaoDoTomcat.Padrao))
            .AddSingleton<RepositorioDoTomcat>()
            .AddSingleton<ILancadorDeProcessos, LancadorDeProcessos>()
            .AddSingleton<IPortasDoSistema, PortasDoSistema>()
            .AddSingleton<PreparadorDoTomcat>()
            .AddSingleton<GerenteDeTomcat>();
}
