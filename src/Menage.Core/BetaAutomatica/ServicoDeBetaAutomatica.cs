using System.Globalization;
using Menage.Core.Beta;

namespace Menage.Core.BetaAutomatica;

/// <summary>
/// Beta automática: monitora o Jira, decide quais atividades entram, confere a branch (sem trocar nada nos projetos de trabalho)
/// e gera a beta em clones isolados, uma atividade por vez. Estado em <c>beta-automatica.json</c>.
/// Esta classe tem o estado e o monitor; o ciclo, as ações do usuário, a geração e o diagnóstico estão nos outros arquivos parciais.
/// </summary>
public sealed partial class ServicoDeBetaAutomatica : IDisposable
{
    readonly IArmazenamento<ConfigBetaAuto> _armazenamento;
    readonly IFabricaDeJira _fabrica;
    readonly VerificadorDeBranches _verificador;
    readonly IProjetos _projetos;
    readonly RepositorioDoGerador _gerador;
    readonly IPerfilAtual _perfil;
    readonly ConsoleDaBeta _console;
    readonly PastaDeDados _pasta;
    readonly object _trava = new();
    readonly SemaphoreSlim _sinalDoCiclo = new(0);
    CancellationTokenSource? _parar;
    Task? _monitor;
    volatile bool _ciclando;

    public ServicoDeBetaAutomatica(
        IArmazenamento<ConfigBetaAuto> armazenamento, IFabricaDeJira fabrica, VerificadorDeBranches verificador, IProjetos projetos,
        RepositorioDoGerador gerador, IPerfilAtual perfil, ConsoleDaBeta console, PastaDeDados pasta, PreparadorDeClone clones,
        PreparadorDeGeracao preparador, IProcessoRunner runner)
    {
        _armazenamento = armazenamento;
        _fabrica = fabrica;
        _verificador = verificador;
        _projetos = projetos;
        _gerador = gerador;
        _perfil = perfil;
        _console = console;
        _pasta = pasta;
        _clones = clones;
        _preparador = preparador;
        _runner = runner;
        Config = armazenamento.Carregar().ValorOu(new ConfigBetaAuto());
    }

    public ConfigBetaAuto Config { get; private set; }

    public bool ModoTester => _perfil.Atual == PerfilDeUso.Tester;

    public bool Ocupado => _ciclando;

    public bool Rodando => _monitor is { IsCompleted: false };

    public string UltimoResumo { get; private set; } = "";

    /// <summary>Uma atividade ficou pronta para gerar (alerta).</summary>
    public event Action<ItemBeta>? ItemPronto;

    public event Action<ItemBeta, bool>? GeracaoConcluida;

    public event Action<string>? Mensagem;

    /// <summary>Qualquer mudança no estado: a tela se atualiza.</summary>
    public event Action? Mudou;

    public string ArquivoDoLog => Path.Combine(_pasta.Caminho, "logs-beta-auto", "monitor.log");

    public IReadOnlyList<ItemBeta> Itens()
    {
        lock (_trava) return [.. Config.Itens.Where(i => i.Estado is not (EstadoDoItem.Ignorada or EstadoDoItem.Finalizada)).OrderByDescending(i => i.DetectadaEm)];
    }

    public IReadOnlyList<ItemBeta> Ignoradas()
    {
        lock (_trava) return [.. Config.Itens.Where(i => i.Estado == EstadoDoItem.Ignorada).OrderByDescending(i => i.IgnoradaEm)];
    }

    public IReadOnlyList<ItemBeta> Finalizadas()
    {
        lock (_trava) return [.. Config.Itens.Where(i => i.Estado == EstadoDoItem.Finalizada).OrderByDescending(i => i.FinalizadaEm)];
    }

    public int Pendentes()
    {
        lock (_trava) return Config.Itens.Count(i => i.Estado is EstadoDoItem.Pronta or EstadoDoItem.Problema or EstadoDoItem.Aguardando);
    }

    public ItemBeta? Achar(string chave)
    {
        lock (_trava) return Config.Achar(chave);
    }

    /// <summary>Altera a configuração e grava. O resto do serviço relê a cada ciclo.</summary>
    public Resultado<Nada> Alterar(Action<ConfigBetaAuto> mudar)
    {
        lock (_trava) mudar(Config);
        return Salvar();
    }

    public void DefinirJqls(IEnumerable<JqlBeta> efetivas)
    {
        lock (_trava) Config.Jqls = GerenciadorDeJql.ParaGravar(Config, efetivas, ModoTester);
        _console.Registrar(NivelDoConsole.Info, "Beta automática", "", "", $"JQL da busca alteradas: {Config.Jqls.Count} diferença(s) do padrão");
        Salvar();
    }

    public List<string> ProjetosConhecidos() => [.. _gerador.Configuracao.Projetos.Select(p => p.Nome)];

    public void Iniciar()
    {
        if (Rodando) return;
        _parar = new CancellationTokenSource();
        var cancelar = _parar.Token;
        _monitor = Task.Run(async () =>
        {
            while (!cancelar.IsCancellationRequested)
            {
                try { if (Config.Ativo) await CicloAsync(false, cancelar); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Falhou("Beta automática: " + ex.Message); }
                try { await _sinalDoCiclo.WaitAsync(TimeSpan.FromMinutes(Math.Max(1, Config.IntervaloMin)), cancelar); }
                catch (OperationCanceledException) { break; }
            }
        }, cancelar);
    }

    public void Parar()
    {
        _parar?.Cancel();
        _sinalDoCiclo.Release();
    }

    public void Dispose()
    {
        Parar();
        _geracaoAtual?.Cancel();
        _sinalDoCiclo.Dispose();
    }

    /// <summary>"Verificar agora": acorda o monitor antes do intervalo.</summary>
    public void VerificarAgora() => _sinalDoCiclo.Release();

    public Resultado<Nada> Salvar()
    {
        Resultado<Nada> r;
        lock (_trava) r = _armazenamento.Salvar(Config);
        return r;
    }

    void Notificar() => Mudou?.Invoke();

    void Msg(string texto)
    {
        UltimoResumo = texto;
        Mensagem?.Invoke(texto);
    }

    void Falhou(string texto)
    {
        lock (_trava) Config.UltimoErro = texto;
        Msg(texto);
    }

    void Con(NivelDoConsole nivel, string atividade, string projeto, string texto) => _console.Registrar(nivel, "Beta automática", atividade, projeto, texto);

    /// <summary>Uma linha por consulta e por evento em <c>logs-beta-auto\monitor.log</c> (e espelho no console).</summary>
    void RegistrarLog(string linha)
    {
        var chave = System.Text.RegularExpressions.Regex.Match(linha, @"^([A-Za-z][A-Za-z0-9]*-\d+)");
        var nivel = linha.Contains("ERRO", StringComparison.Ordinal) || linha.StartsWith("FALHOU", StringComparison.Ordinal) ? NivelDoConsole.Erro : NivelDoConsole.Info;
        _console.Registrar(nivel, "Monitor", chave.Success ? chave.Groups[1].Value : "", "", linha);
        try
        {
            lock (_trava)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ArquivoDoLog)!);
                var info = new FileInfo(ArquivoDoLog);
                if (info.Exists && info.Length > 400_000)
                {
                    var todas = File.ReadAllLines(ArquivoDoLog);
                    File.WriteAllLines(ArquivoDoLog, todas.Skip(todas.Length / 2));
                }

                File.AppendAllText(ArquivoDoLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {linha}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static string Agora() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
