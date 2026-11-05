using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Modulos.JanelaPrincipal;
using Menage.Core;
using Menage.App.Modulos.Perfis;
using Menage.App.Tema;
using Menage.Core.Configuracoes;
using Menage.Core.Interacao;
using Menage.Core.Perfis;
using Menage.Core.Projetos;
using Menage.Core.Protecao;
using Menage.Core.Repos;

namespace Menage.App.Modulos.Configuracoes;

public sealed record OpcaoDeLista(string Id, string Nome);

/// <summary>
/// Janela Configurações: uma tela só, várias seções, um botão Salvar. A tela só edita cópias e entrega tudo aos serviços do
/// Core (que validam e gravam); nada de regra de configuração mora aqui.
/// </summary>
public sealed partial class ConfiguracoesViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> Secoes =
        ["Perfil de uso", "Git", "Caminhos", "Jira e credenciais", "Proteção de branches", "Comportamento", "Importar do Git Manager"];

    readonly ServicoDeConfiguracoes _configuracoes;
    readonly ServicoDePerfis _perfis;
    readonly ProtecaoDeBranches _protecao;
    readonly IConfiguracaoDeProjetos _projetos;
    readonly PreferenciasDaJanelaServico _preferencias;
    readonly IServicoDeTema _temaServico;
    readonly IServicoDeJanelas _janelas;
    readonly IServicoDeDialogos _dialogos;
    readonly InformacoesDoAmbiente _ambiente;

    public ConfiguracoesViewModel(
        ServicoDeConfiguracoes configuracoes, ServicoDePerfis perfis, ProtecaoDeBranches protecao, IConfiguracaoDeProjetos projetos,
        PreferenciasDaJanelaServico preferencias, IServicoDeTema tema, ICatalogoDeTemas temas, CofreDeCredenciais cofre,
        MigradorDoGitManager migrador, IServicoDeJanelas janelas, IServicoDeDialogos dialogos, InformacoesDoAmbiente ambiente)
    {
        (_configuracoes, _perfis, _protecao, _projetos, _preferencias) = (configuracoes, perfis, protecao, projetos, preferencias);
        (_temaServico, _janelas, _dialogos, _ambiente, _cofre, _migrador) = (tema, janelas, dialogos, ambiente, cofre, migrador);
        Temas = temas.Todos.Select(t => new OpcaoDeLista(t.Id, t.Nome)).ToList();
        Diffs = FerramentasDeDiff.Todas.Select(d => new OpcaoDeLista(d, FerramentasDeDiff.Nome(d))).ToList();
        _perfis.Alterado += (_, _) => RecarregarPerfis();
        Carregar();
        _ = CarregarAmbienteAsync();
    }

    public IReadOnlyList<OpcaoDeLista> Temas { get; }

    public IReadOnlyList<OpcaoDeLista> Diffs { get; }

    public ObservableCollection<PerfilDefinido> Perfis { get; } = [];

    // ---- Perfil de uso
    [ObservableProperty]
    PerfilDefinido? _perfilEscolhido;

    public string DescricaoDoPerfil => PerfilEscolhido?.Descricao ?? "";

    partial void OnPerfilEscolhidoChanged(PerfilDefinido? value) => OnPropertyChanged(nameof(DescricaoDoPerfil));

    // ---- Git
    [ObservableProperty]
    OpcaoDeLista? _diff;

    [ObservableProperty]
    bool _fetchAutomatico;

    [ObservableProperty]
    string _fetchMinutos = "10";

    [ObservableProperty]
    bool _fetchComTags;

    [ObservableProperty]
    string _versaoDoGit = "git: verificando...";

    [ObservableProperty]
    string _modoSsh = "";

    // ---- Caminhos
    [ObservableProperty]
    string _raiz = "";

    [ObservableProperty]
    string _chaveSsh = "";

    [ObservableProperty]
    string _winMerge = "";

    [ObservableProperty]
    string _tortoiseGit = "";

    // ---- Jira
    [ObservableProperty]
    string _jiraUrl = "";

    [ObservableProperty]
    string _jiraProjetos = "";

    [ObservableProperty]
    string _jiraVariavel = "";

    // ---- Proteção
    [ObservableProperty]
    bool _bloquearPush;

    [ObservableProperty]
    bool _bloquearExclusao;

    [ObservableProperty]
    string _branchesProtegidas = "";

    [ObservableProperty]
    bool _validarCommit;

    [ObservableProperty]
    bool _aceitarTicket;

    [ObservableProperty]
    string _tiposDeCommit = "";

    // ---- Comportamento
    [ObservableProperty]
    bool _verificarAtualizacao;

    [ObservableProperty]
    bool _scriptExcluir;

    [ObservableProperty]
    string _scriptPadrao = "";

    [ObservableProperty]
    string _scriptDestinos = "";

    [ObservableProperty]
    bool _saidaRecolhida;

    [ObservableProperty]
    OpcaoDeLista? _tema;

    // ---- Estado da tela
    [ObservableProperty]
    string _mensagem = "";

    [ObservableProperty]
    bool _mensagemEhErro;

    partial void OnTemaChanged(OpcaoDeLista? value)
    {
        if (value is not null && value.Id != _temaServico.Atual.Id) _temaServico.Trocar(value.Id);
    }

    [RelayCommand]
    void GerenciarPerfis() => _janelas.Abrir(JanelaDePerfis.Id);

    [RelayCommand]
    void RestaurarProtecao()
    {
        BloquearPush = BloquearExclusao = AceitarTicket = true;
        BranchesProtegidas = string.Join(Environment.NewLine, ConfigDeProtecao.BranchesPadrao);
        TiposDeCommit = string.Join(", ", ConfigDeProtecao.TiposPadrao);
        ValidarCommit = false;
        Avisar("Proteção com os valores padrão. Salve para valer.");
    }

    [RelayCommand]
    void Descartar()
    {
        Carregar();
        Avisar("Alterações descartadas.");
    }

    [RelayCommand]
    void Salvar()
    {
        if (!int.TryParse(FetchMinutos.Trim(), out var minutos)) { Avisar("O intervalo do fetch deve ser um número de minutos (1 a 240).", true); return; }
        var geral = MontarGerais(minutos);
        var protecao = MontarProtecao();
        if (geral.Validar() is { } invalida) { Avisar(invalida.ToString(), true); return; }
        if (ValidarCommit && protecao.Tipos.Count == 0) { Avisar("Informe ao menos um tipo de commit (feat, fix...).", true); return; }

        var falhas = new List<string>();
        void Aplicar(string quem, Resultado<Nada> r) { if (!r.EhOk) falhas.Add($"{quem}: {r.Erro}"); }
        Aplicar("Configurações", _configuracoes.Salvar(geral));
        Aplicar("Proteção de branches", _protecao.Salvar(protecao));
        if (PerfilEscolhido is { } perfil && perfil.Id != _perfis.Ativo.Id) Aplicar("Perfil de uso", _perfis.Ativar(perfil.Id));
        _preferencias.Alterar(p => p with { FetchAutomatico = FetchAutomatico, SaidaRecolhida = SaidaRecolhida });

        if (falhas.Count == 0) Avisar("Configurações salvas.");
        else Avisar(string.Join(" | ", falhas), true);
    }

    ConfiguracoesGerais MontarGerais(int minutos) => _configuracoes.Atual with
    {
        Git = new ConfigDeGit { FerramentaDeDiff = Diff?.Id ?? FerramentasDeDiff.Interno, FetchMinutos = minutos, FetchComTags = FetchComTags },
        Caminhos = new ConfigDeCaminhos { ChaveSsh = ChaveSsh.Trim(), WinMerge = WinMerge.Trim(), TortoiseGit = TortoiseGit.Trim() },
        Jira = new ConfigDoJira { Url = JiraUrl.Trim().TrimEnd('/'), Usuario = _configuracoes.Atual.Jira.Usuario, VariavelDoToken = JiraVariavel.Trim(), Projetos = JiraProjetos.Trim() },
        Comportamento = new ConfigDeComportamento
        {
            VerificarAtualizacaoAoAbrir = VerificarAtualizacao,
            ScriptDaAtividade = new ConfigDoScriptDaAtividade { Excluir = ScriptExcluir, Padrao = ScriptPadrao.Trim(), Destinos = Separar(ScriptDestinos, ',') },
        },
    };

    ConfigDeProtecao MontarProtecao() => new()
    {
        BloquearPush = BloquearPush,
        BloquearExclusao = BloquearExclusao,
        Branches = Separar(BranchesProtegidas, '\n', ','),
        ValidarCommit = ValidarCommit,
        AceitarTicket = AceitarTicket,
        Tipos = Separar(TiposDeCommit, ',', '\n'),
    };

    static List<string> Separar(string texto, params char[] separadores) =>
        texto.Replace("\r", "").Split(separadores, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    void Carregar()
    {
        var c = _configuracoes.Atual;
        RecarregarPerfis();
        Diff = Diffs.FirstOrDefault(d => d.Id == c.Git.FerramentaDeDiff) ?? Diffs[0];
        FetchAutomatico = _preferencias.Valor.FetchAutomatico;
        FetchMinutos = c.Git.FetchMinutos.ToString(System.Globalization.CultureInfo.InvariantCulture);
        FetchComTags = c.Git.FetchComTags;
        Raiz = _projetos.Catalogo.Raiz;
        ChaveSsh = c.Caminhos.ChaveSsh;
        WinMerge = c.Caminhos.WinMerge;
        TortoiseGit = c.Caminhos.TortoiseGit;
        JiraUrl = c.Jira.Url;
        JiraProjetos = c.Jira.Projetos;
        JiraVariavel = c.Jira.VariavelDoToken;
        var p = _protecao.Atual;
        (BloquearPush, BloquearExclusao, ValidarCommit, AceitarTicket) = (p.BloquearPush, p.BloquearExclusao, p.ValidarCommit, p.AceitarTicket);
        BranchesProtegidas = string.Join(Environment.NewLine, p.Branches);
        TiposDeCommit = string.Join(", ", p.Tipos);
        VerificarAtualizacao = c.Comportamento.VerificarAtualizacaoAoAbrir;
        (ScriptExcluir, ScriptPadrao, ScriptDestinos) = (c.Comportamento.ScriptDaAtividade.Excluir, c.Comportamento.ScriptDaAtividade.Padrao, string.Join(", ", c.Comportamento.ScriptDaAtividade.Destinos));
        SaidaRecolhida = _preferencias.Valor.SaidaRecolhida;
        Tema = Temas.FirstOrDefault(t => t.Id == _temaServico.Atual.Id);
        CarregarCredenciais();
    }

    void RecarregarPerfis()
    {
        Perfis.Clear();
        foreach (var p in _perfis.Perfis) Perfis.Add(p);
        PerfilEscolhido = Perfis.FirstOrDefault(p => p.Id == _perfis.Ativo.Id);
    }

    async Task CarregarAmbienteAsync()
    {
        VersaoDoGit = await _ambiente.VersaoDoGitAsync();
        ModoSsh = await _ambiente.ModoSshAsync();
    }

    /// <summary>Troca a pasta raiz (confirmada pela tela) e procura os projetos de novo.</summary>
    public async Task DefinirRaizAsync(string novaRaiz)
    {
        if (string.Equals(novaRaiz, Raiz, StringComparison.OrdinalIgnoreCase)) return;
        var previa = new Previa("Trocar a pasta raiz dos projetos?", $"Nova raiz: {novaRaiz}", ["Os projetos reconhecidos (web, framework, pda) serão procurados de novo."]);
        if (!await _dialogos.ConfirmarAsync(previa)) return;
        var r = _projetos.SalvarRaiz(novaRaiz);
        if (r.EhOk) Raiz = _projetos.Catalogo.Raiz;
        Avisar(r.EhOk ? $"Raiz alterada: {_projetos.Projetos.Count} projeto(s) encontrado(s)." : r.Erro.ToString(), !r.EhOk);
    }

    void Avisar(string texto, bool erro = false)
    {
        Mensagem = texto;
        MensagemEhErro = erro;
    }
}
