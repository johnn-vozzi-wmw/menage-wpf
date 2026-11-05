using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using Menage.Core;
using Menage.Core.Interacao;
using Menage.Core.Nexus;
using Menage.Core.Tomcat;

namespace Menage.App.Modulos.Tomcat;

public sealed partial class LinhaDeAmbiente(AmbienteTomcat ambiente) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo), nameof(Detalhe))]
    AmbienteTomcat _ambiente = ambiente;

    [ObservableProperty]
    SituacaoDoTomcat _situacao = SituacaoDoTomcat.Parado;

    [ObservableProperty]
    string _resumoDasApps = "";

    public string Titulo => Ambiente.Titulo;

    public string Detalhe => $"porta {Ambiente.Porta}  •  vendas {Vazio(Ambiente.InstaladoVendas)}  •  dados {Vazio(Ambiente.InstaladoDados)}";

    public string SituacaoTexto => PreparadorDoTomcat.Nome(Situacao);

    partial void OnSituacaoChanged(SituacaoDoTomcat value) => OnPropertyChanged(nameof(SituacaoTexto));

    static string Vazio(string v) => v.Length > 0 ? v : "-";
}

/// <summary>Ambientes Tomcat: cadastro, preparar (baixar e trocar os .war), subir, parar e acompanhar o log. Regras no Core.</summary>
public sealed partial class TomcatViewModel(
    RepositorioDoTomcat repositorio, RepositorioDoNexus nexus, GerenteDeTomcat gerente, PreparadorDoTomcat preparador,
    IServicoDeDialogos dialogos, IServicoDeJanelas janelas) : ObservableObject, IDisposable
{
    const int LimiteDoLog = 3000;
    CancellationTokenSource? _cancelarPreparo;
    RelatorDePreparo? _relator;
    int _passosLidos;
    long _indiceDoLog;
    string? _logDe;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrepararCommand), nameof(SubirCommand), nameof(PararCommand), nameof(SalvarCommand), nameof(ExcluirCommand))]
    LinhaDeAmbiente? _selecionada;

    [ObservableProperty]
    string _nome = "";

    [ObservableProperty]
    string _pasta = "";

    [ObservableProperty]
    string _porta = "8082";

    [ObservableProperty]
    string _grupo = "";

    [ObservableProperty]
    string _repositorioDoNexus = "";

    [ObservableProperty]
    string _versaoVendas = "";

    [ObservableProperty]
    string _versaoDados = "";

    [ObservableProperty]
    string _backups = "2";

    [ObservableProperty]
    string _mensagem = "Escolha ou crie um ambiente.";

    [ObservableProperty]
    bool _mensagemComErro;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrepararCommand), nameof(SubirCommand))]
    bool _preparando;

    [ObservableProperty]
    string _etapa = "";

    [ObservableProperty]
    double _progressoVendas;

    [ObservableProperty]
    double _progressoDados;

    [ObservableProperty]
    string _textoVendas = "";

    [ObservableProperty]
    string _textoDados = "";

    public ObservableCollection<LinhaDeAmbiente> Ambientes { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    public ObservableCollection<string> VersoesVendas { get; } = [];

    public ObservableCollection<string> VersoesDados { get; } = [];

    /// <summary>Galeria: não busca nada nem lê arquivos de ambiente.</summary>
    public bool ModoExemplo { get; set; }

    public bool TemSelecionada => Selecionada is not null;

    public void Iniciar()
    {
        Ambientes.Clear();
        foreach (var a in repositorio.Configuracao.Ambientes) Adicionar(a);
        Selecionada = Ambientes.FirstOrDefault(l => l.Ambiente.Id == repositorio.Configuracao.Selecionado) ?? Ambientes.FirstOrDefault();
    }

    public void ColocarExemplo(IEnumerable<(AmbienteTomcat Ambiente, SituacaoDoTomcat Situacao, string Apps)> itens, IEnumerable<string> log)
    {
        Ambientes.Clear();
        foreach (var (a, s, apps) in itens)
        {
            var linha = new LinhaDeAmbiente(a) { Situacao = s, ResumoDasApps = apps };
            Ambientes.Add(linha);
        }

        Selecionada = Ambientes.FirstOrDefault();
        Log.Clear();
        foreach (var l in log) Log.Add(l);
        Etapa = "Etapa 3 de 7: baixando wmwvendas.war e wmwdados.war";
        (ProgressoVendas, ProgressoDados) = (0.62, 0.35);
        (TextoVendas, TextoDados) = ("wmwvendas.war 9.060-3: 41,2 MB de 66,0 MB", "wmwdados.war 9.060-3: 12,1 MB de 34,5 MB");
    }

    partial void OnSelecionadaChanged(LinhaDeAmbiente? value)
    {
        OnPropertyChanged(nameof(TemSelecionada));
        VersoesVendas.Clear();
        VersoesDados.Clear();
        Log.Clear();
        _indiceDoLog = 0;
        _logDe = null;
        if (value is null) return;
        var a = value.Ambiente;
        (Nome, Pasta, Porta, Grupo, RepositorioDoNexus, VersaoVendas, VersaoDados, Backups) =
            (a.Nome, a.Pasta, a.Porta.ToString(CultureInfo.InvariantCulture), a.Grupo, a.Repositorio, a.VersaoVendas, a.VersaoDados, a.ManterBackups.ToString(CultureInfo.InvariantCulture));
    }

    void Adicionar(AmbienteTomcat a)
    {
        var linha = new LinhaDeAmbiente(a);
        if (!ModoExemplo) Execucao(linha).AmbienteMudou += novo => AoMudarAmbiente(linha, novo);
        Ambientes.Add(linha);
        if (!ModoExemplo) linha.Situacao = Execucao(linha).Situacao();
    }

    ExecucaoDoTomcat Execucao(LinhaDeAmbiente linha) => gerente.De(linha.Ambiente);

    void AoMudarAmbiente(LinhaDeAmbiente linha, AmbienteTomcat novo)
    {
        linha.Ambiente = novo;
        Gravar();
    }

    void Gravar() =>
        repositorio.Salvar(new ConfiguracaoDoTomcat { Ambientes = [.. Ambientes.Select(l => l.Ambiente)], Selecionado = Selecionada?.Ambiente.Id ?? "" });

    [RelayCommand]
    void Novo()
    {
        var novo = new AmbienteTomcat { Nome = $"Ambiente {Ambientes.Count + 1}", Porta = 8082 + Ambientes.Count };
        Adicionar(novo);
        Selecionada = Ambientes[^1];
        Gravar();
        Dizer("Ambiente criado. Escolha a pasta do Tomcat e salve.", erro: false);
    }

    [RelayCommand(CanExecute = nameof(TemSelecionada))]
    async Task SalvarAsync()
    {
        if (Selecionada is null) return;
        if (!int.TryParse(Porta, NumberStyles.None, CultureInfo.InvariantCulture, out var porta) || porta is < 1024 or > 65535)
        {
            Dizer("Porta inválida: use um número entre 1024 e 65535.", erro: true);
            return;
        }

        _ = int.TryParse(Backups, NumberStyles.None, CultureInfo.InvariantCulture, out var backups);
        var novo = Selecionada.Ambiente with
        {
            Nome = Nome.Trim(), Pasta = Pasta.Trim(), Porta = porta, Grupo = Grupo.Trim(), Repositorio = RepositorioDoNexus.Trim(),
            VersaoVendas = VersaoVendas.Trim(), VersaoDados = VersaoDados.Trim(), ManterBackups = Math.Clamp(backups, 0, 20),
        };
        var config = new ConfiguracaoDoTomcat { Ambientes = [.. Ambientes.Select(l => l == Selecionada ? novo : l.Ambiente)] };
        if (EscolhaDePortas.DonoDaPasta(config, novo) is { } dono)
        {
            await dialogos.InformarAsync("Pasta já usada", $"O ambiente \"{dono.Titulo}\" já usa esta pasta. O mesmo Tomcat em dois ambientes se atropela.");
            return;
        }

        if (EscolhaDePortas.DosOutros(config, novo).Contains(porta))
        {
            await dialogos.InformarAsync("Porta já usada", $"A porta {porta} já é de outro ambiente (HTTP, parada ou AJP).");
            return;
        }

        Selecionada.Ambiente = novo;
        Gravar();
        var problema = PastaDoTomcat.Validar(novo.Pasta);
        Dizer(problema is null ? $"Salvo. {PastaDoTomcat.Versao(novo.Pasta)}" : $"Salvo, mas: {problema.Causa}. {problema.AcaoSugerida}", erro: problema is not null);
    }

    [RelayCommand(CanExecute = nameof(TemSelecionada))]
    async Task ExcluirAsync()
    {
        if (Selecionada is null) return;
        if (Execucao(Selecionada).Vivo)
        {
            await dialogos.InformarAsync("Ambiente em execução", "Pare o Tomcat antes de remover o ambiente. Fechar o Menage não para os Tomcats.");
            return;
        }

        if (!await dialogos.ConfirmarAsync(new Previa("Remover ambiente", $"Remover \"{Selecionada.Titulo}\" da lista?", ["Os arquivos do Tomcat não são apagados."]))) return;
        gerente.Esquecer(Selecionada.Ambiente.Id);
        var remover = Selecionada;
        Selecionada = null;
        Ambientes.Remove(remover);
        Gravar();
        Selecionada = Ambientes.FirstOrDefault();
    }

    bool PodePreparar() => Selecionada is not null && !Preparando;

    [RelayCommand(CanExecute = nameof(PodePreparar))]
    async Task PrepararAsync()
    {
        await SalvarAsync();
        var linha = Selecionada;
        if (linha is null) return;
        var exec = Execucao(linha);
        var cliente = nexus.Cliente();
        var config = new ConfiguracaoDoTomcat { Ambientes = [.. Ambientes.Select(l => l.Ambiente)] };
        _relator = new RelatorDePreparo();
        _passosLidos = 0;
        _cancelarPreparo = new CancellationTokenSource();
        Preparando = true;
        Log.Clear();
        try
        {
            var r = await preparador.PrepararAsync(linha.Ambiente, cliente.EhOk ? cliente.Valor : null, nexus.Configuracao.Repositorio,
                EscolhaDePortas.DosOutros(config, linha.Ambiente), exec.Situacao(), _relator, _cancelarPreparo.Token);
            LerPassos();
            if (r.EhOk)
            {
                linha.Ambiente = r.Valor;
                Gravar();
                Dizer("Ambiente pronto para subir.", erro: false);
            }
            else
            {
                Dizer(r.Erro == Erro.Cancelado ? "Preparo cancelado. Nada foi trocado." : r.Erro.ToString(), erro: r.Erro != Erro.Cancelado);
            }
        }
        finally
        {
            Preparando = false;
            _cancelarPreparo.Dispose();
            _cancelarPreparo = null;
            Etapa = "";
        }
    }

    [RelayCommand]
    void CancelarPreparo()
    {
        if (_relator?.PodeCancelar == true) _cancelarPreparo?.Cancel();
    }

    bool PodeSubir() => Selecionada is not null && !Preparando && Selecionada.Situacao is SituacaoDoTomcat.Parado or SituacaoDoTomcat.PortaOcupada;

    [RelayCommand(CanExecute = nameof(PodeSubir))]
    async Task SubirAsync()
    {
        var linha = Selecionada!;
        await SalvarAsync();
        var config = new ConfiguracaoDoTomcat { Ambientes = [.. Ambientes.Select(l => l.Ambiente)] };
        var r = await Execucao(linha).SubirAsync(EscolhaDePortas.DosOutros(config, linha.Ambiente), preparador);
        Dizer(r.EhOk ? "Subindo o Tomcat..." : r.Erro.ToString(), erro: !r.EhOk);
        AtualizarSituacoes();
    }

    bool PodeParar() => Selecionada is not null && Selecionada.Situacao is SituacaoDoTomcat.Subindo or SituacaoDoTomcat.NoAr;

    [RelayCommand(CanExecute = nameof(PodeParar))]
    async Task PararAsync()
    {
        var linha = Selecionada!;
        Dizer("Parando...", erro: false);
        var texto = await Execucao(linha).PararAsync(TimeSpan.FromSeconds(40));
        Dizer(texto, erro: false);
        AtualizarSituacoes();
    }

    [RelayCommand]
    void AbrirNoNavegador(string? contexto)
    {
        if (Selecionada is null || contexto is null) return;
        Process.Start(new ProcessStartInfo(Selecionada.Ambiente.Url(contexto)) { UseShellExecute = true });
    }

    [RelayCommand]
    void AbrirPasta()
    {
        if (Selecionada is { } l && Directory.Exists(l.Ambiente.Pasta)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{l.Ambiente.Pasta}\"") { UseShellExecute = true });
    }

    [RelayCommand(CanExecute = nameof(TemSelecionada))]
    void AbrirBanco()
    {
        if (Selecionada is not { } l) return;
        if (!Directory.Exists(l.Ambiente.Pasta)) { Dizer("A pasta do Tomcat não existe. Escolha a pasta na tela e salve antes de editar o banco.", erro: true); return; }
        janelas.Abrir(JanelaDoBancoTomcat.Id, l.Ambiente.Pasta);
    }

    [RelayCommand]
    void AbrirNexus() => janelas.Abrir(Nexus.JanelaDoNexus.Id);

    [RelayCommand]
    async Task BuscarVersoesAsync()
    {
        var cliente = nexus.Cliente();
        if (Selecionada is null || !cliente.EhOk)
        {
            Dizer(cliente.EhOk ? "Escolha um ambiente." : cliente.Erro.ToString(), erro: true);
            return;
        }

        Dizer("Buscando versões no Nexus...", erro: false);
        var a = Selecionada.Ambiente;
        var repo = RepositorioDoNexus.Length > 0 ? RepositorioDoNexus : nexus.Configuracao.Repositorio;
        var vendas = await ResolvedorDeWar.VersoesAsync(cliente.Valor, repo, a.NomeVendas, Grupo);
        var dados = await ResolvedorDeWar.VersoesAsync(cliente.Valor, repo, a.NomeDados, Grupo);
        Preencher(VersoesVendas, vendas);
        Preencher(VersoesDados, dados);
        Dizer(vendas.EhOk && dados.EhOk ? $"{VersoesVendas.Count} versões de {a.NomeVendas} e {VersoesDados.Count} de {a.NomeDados}." : (vendas.EhOk ? dados : vendas).Erro.ToString(), erro: !(vendas.EhOk && dados.EhOk));
    }

    static void Preencher(ObservableCollection<string> destino, Resultado<IReadOnlyList<string>> origem)
    {
        destino.Clear();
        if (!origem.EhOk) return;
        foreach (var v in origem.Valor) destino.Add(v);
    }

    /// <summary>Chamado a cada segundo pela janela: situação, saúde das apps e novas linhas do log.</summary>
    public async Task TiqueAsync()
    {
        if (ModoExemplo) return;
        AtualizarSituacoes();
        if (Preparando && _relator is { } r)
        {
            LerPassos();
            (Etapa, ProgressoVendas, ProgressoDados) = ($"Etapa {r.EtapaNumero} de {r.EtapaTotal}: {r.EtapaAtual}", Razao(r, 0), Razao(r, 1));
            (TextoVendas, TextoDados) = (Texto(r, 0), Texto(r, 1));
            return;
        }

        if (Selecionada is not { } linha) return;
        var exec = Execucao(linha);
        if (_logDe != linha.Ambiente.Id) (_logDe, _indiceDoLog) = (linha.Ambiente.Id, 0);
        exec.AtualizarCauda();
        var novas = exec.LinhasDesde(_indiceDoLog, out var proximo);
        foreach (var l in novas) Log.Add(l);
        _indiceDoLog = proximo;
        while (Log.Count > LimiteDoLog) Log.RemoveAt(0);
        await exec.VerificarSaudeAsync();
        linha.ResumoDasApps = exec.ResumoDasApps();
        if (exec.Interpretador.ErroDetectado.Length > 0 && linha.Situacao != SituacaoDoTomcat.NoAr) Dizer(exec.Interpretador.ErroDetectado, erro: true);
    }

    void AtualizarSituacoes()
    {
        foreach (var l in Ambientes) l.Situacao = Execucao(l).Situacao();
        PrepararCommand.NotifyCanExecuteChanged();
        SubirCommand.NotifyCanExecuteChanged();
        PararCommand.NotifyCanExecuteChanged();
    }

    void LerPassos()
    {
        if (_relator is null) return;
        var passos = _relator.Passos;
        for (; _passosLidos < passos.Count; _passosLidos++) Log.Add(passos[_passosLidos]);
    }

    static double Razao(RelatorDePreparo r, int i) => r.Totais[i] > 0 ? Math.Min(1, (double)r.Feito(i) / r.Totais[i]) : 0;

    static string Texto(RelatorDePreparo r, int i) =>
        r.Nomes[i] is null ? "" : $"{r.Nomes[i]} {r.Versoes[i]}: {PastaDoTomcat.Formatar(r.Feito(i))} de {PastaDoTomcat.Formatar(r.Totais[i])}";

    void Dizer(string texto, bool erro) => (Mensagem, MensagemComErro) = (texto, erro);

    public void Dispose() => _cancelarPreparo?.Dispose();
}
