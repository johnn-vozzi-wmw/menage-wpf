using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Modulos.GeradorBeta;
using Menage.Core.BetaAutomatica;
using Menage.Core.Interacao;

namespace Menage.App.Modulos.BetaAutomatica;

/// <summary>Tudo o que a aba Configurar edita, em texto; igualdade por valor para saber se há alteração pendente.</summary>
public sealed record ValoresDaConfig(
    string Jira, string Variavel, string Chaves, string Intervalo, bool EscopoQualquer, string Alvo, string Origem, string Dev, string Revisao,
    string Finais, string Tipos, string Pasta);

/// <summary>
/// Aba Configurar: os campos do monitor (Jira, status, intervalo, pasta dos clones, escopo do Tester). Salvar e Cancelar só valem quando há
/// alteração; a regra de gravar está em <see cref="ServicoDeBetaAutomatica.Alterar"/>. Campos do outro perfil ficam como estão.
/// </summary>
public sealed partial class ConfigBetaViewModel : ObservableObject
{
    readonly ServicoDeBetaAutomatica _servico;
    readonly IServicoDeJanelas _janelas;
    ValoresDaConfig _base = null!;
    bool _carregando;

    [ObservableProperty]
    string _jiraUrl = "";

    [ObservableProperty]
    string _variavelDoToken = "";

    [ObservableProperty]
    string _intervalo = "";

    [ObservableProperty]
    bool _escopoQualquer;

    [ObservableProperty]
    string _statusAlvo = "";

    [ObservableProperty]
    string _statusOrigem = "";

    [ObservableProperty]
    string _statusDev = "";

    [ObservableProperty]
    string _statusRevisao = "";

    [ObservableProperty]
    string _pastaTrabalho = "";

    [ObservableProperty]
    string _aviso = "";

    [ObservableProperty]
    string _mensagem = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SalvarCommand), nameof(CancelarCommand))]
    bool _alterado;

    public ConfigBetaViewModel(ServicoDeBetaAutomatica servico, IServicoDeJanelas janelas)
    {
        (_servico, _janelas) = (servico, janelas);
        Chaves.CollectionChanged += AoMudarLista;
        StatusFinais.CollectionChanged += AoMudarLista;
        TiposCoding.CollectionChanged += AoMudarLista;
        Carregar();
    }

    /// <summary>Gravou: a janela atualiza o status do monitor (intervalo etc.).</summary>
    public event Action? Salvo;

    public ObservableCollection<string> Chaves { get; } = [];

    public ObservableCollection<string> StatusFinais { get; } = [];

    public ObservableCollection<string> TiposCoding { get; } = [];

    public bool ModoTester => _servico.ModoTester;

    public bool ModoDesenvolvedor => !_servico.ModoTester;

    public bool EscopoAtribuidas
    {
        get => !EscopoQualquer;
        set { if (value) EscopoQualquer = false; }
    }

    /// <summary>Relê o que está gravado e descarta o que estava pendente (abertura da janela e Cancelar).</summary>
    public void Carregar()
    {
        _carregando = true;
        var c = _servico.Config;
        JiraUrl = c.JiraUrl;
        VariavelDoToken = c.VariavelDoToken;
        Preencher(Chaves, ListaSeparada.Ler(c.ChavesJira, separarPorEspaco: true));
        Intervalo = c.IntervaloMin.ToString(CultureInfo.InvariantCulture);
        EscopoQualquer = c.TesterEscopo.Equals("todas", StringComparison.OrdinalIgnoreCase);
        StatusAlvo = c.StatusAlvo;
        StatusOrigem = c.StatusCodeReview;
        StatusDev = c.StatusDev;
        StatusRevisao = c.StatusRevisao;
        Preencher(StatusFinais, c.StatusFinais);
        Preencher(TiposCoding, ListaSeparada.Ler(c.TiposCoding));
        PastaTrabalho = c.PastaTrabalho;
        _carregando = false;
        Aviso = "";
        Mensagem = "";
        _base = Capturar();
        Alterado = false;
        OnPropertyChanged(nameof(ModoTester));
        OnPropertyChanged(nameof(ModoDesenvolvedor));
    }

    static void Preencher(ObservableCollection<string> destino, IEnumerable<string> itens)
    {
        destino.Clear();
        foreach (var i in itens) destino.Add(i);
    }

    ValoresDaConfig Capturar() => new(
        JiraUrl.Trim(), VariavelDoToken.Trim(), ListaSeparada.Juntar(Chaves), Intervalo.Trim(), EscopoQualquer, StatusAlvo.Trim(), StatusOrigem.Trim(),
        StatusDev.Trim(), StatusRevisao.Trim(), ListaSeparada.Juntar(StatusFinais), ListaSeparada.Juntar(TiposCoding), PastaTrabalho.Trim());

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_carregando || e.PropertyName is nameof(Alterado) or nameof(Aviso) or nameof(Mensagem) or nameof(ModoTester) or nameof(ModoDesenvolvedor) or nameof(EscopoAtribuidas)) return;
        if (e.PropertyName == nameof(EscopoQualquer)) OnPropertyChanged(nameof(EscopoAtribuidas));
        Reavaliar();
    }

    void AoMudarLista(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (!_carregando) Reavaliar();
    }

    void Reavaliar()
    {
        Alterado = Capturar() != _base;
        Mensagem = "";
    }

    [RelayCommand(CanExecute = nameof(Alterado))]
    void Cancelar() => Carregar();

    [RelayCommand(CanExecute = nameof(Alterado))]
    void Salvar()
    {
        if (!int.TryParse(Intervalo.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var minutos) || minutos < 1) { Aviso = "O intervalo precisa ser um número de minutos (1 ou mais)."; return; }
        if (StatusAlvo.Trim().Length == 0 || PastaTrabalho.Trim().Length == 0 || JiraUrl.Trim().Length == 0) { Aviso = "Jira, status e pasta dos clones são obrigatórios."; return; }

        var v = Capturar();
        var resultado = _servico.Alterar(c =>
        {
            c.JiraUrl = v.Jira;
            c.VariavelDoToken = v.Variavel;
            c.ChavesJira = v.Chaves;
            c.StatusAlvo = v.Alvo;
            c.StatusFinal = v.Finais;
            c.IntervaloMin = minutos;
            c.PastaTrabalho = v.Pasta;
            c.StatusDev = v.Dev;
            c.StatusRevisao = v.Revisao;
            c.TiposCoding = v.Tipos;
            c.StatusCodeReview = v.Origem;
            c.TesterEscopo = v.EscopoQualquer ? "todas" : "atribuidas";
        });
        if (!resultado.EhOk) { Aviso = resultado.Erro.ToString(); return; }

        Aviso = "";
        _base = v;
        Alterado = false;
        Mensagem = "Configuração salva.";
        Salvo?.Invoke();
    }

    [RelayCommand]
    async Task OrdemDosProjetosAsync()
    {
        var atual = OrdemDeProjetos.Efetiva(_servico.Config.OrdemProjetos);
        if (await _janelas.AbrirDialogoAsync(JanelaDeOrdem.Id, atual) is List<string> nova) _servico.Alterar(c => c.OrdemProjetos = nova);
    }
}
