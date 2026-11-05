using Menage.Core.Comandos;
using Menage.Core.Interacao;
using Menage.Core.Operacoes;
using Menage.Core.Perfis;
using Menage.Core.Projetos;
using Menage.Core.Repos;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Menage.Core.CherryPick;
using Menage.Core.Historico;
using Menage.Core.Operacoes.CherryPick;

namespace Menage.App.Modulos.CherryPick;

/// <summary>
/// Janela Cherry-pick: os projetos marcados, cada um com seu histórico, sob os mesmos filtros. As ações trabalham sobre os commits
/// marcados em todos os painéis: aplicar na hora, guardar em stash ou entrar na fila.
/// </summary>
public sealed partial class JanelaCherryPickViewModel : ObservableObject, IDisposable
{
    public const string IdDaFila = "cherry.fila";

    readonly LeitorDeHistorico _leitor;
    readonly PreparadorDeCherryPick _preparador;
    readonly CherryPickEmLote _cherry;
    readonly StashDeCommitsEmLote _stash;
    readonly ServicoDaFila _fila;
    readonly CoordenadorDeOperacoes _coordenador;
    readonly IServicoDeJanelas _janelas;
    readonly SugestoesDeBranches _sugestoes;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(220) };

    [ObservableProperty]
    string _texto = "";

    [ObservableProperty]
    string _autor = "";

    [ObservableProperty]
    DateTime? _de;

    [ObservableProperty]
    DateTime? _ate;

    [ObservableProperty]
    string _escopo = "";

    [ObservableProperty]
    bool _mostrarMerges;

    [ObservableProperty]
    bool _ladoALado = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AbasVisiveis))]
    PainelDeHistorico? _painelAtual;

    [ObservableProperty]
    string _destino = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeAgir), nameof(TextoCherry), nameof(TextoStash), nameof(TextoFila))]
    int _totalMarcados;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeAgir))]
    bool _ocupado;

    [ObservableProperty]
    int _naFila;

    public JanelaCherryPickViewModel(
        IProjetos projetos, LeitorDeHistorico leitor, LeitorDeDetalhes detalhes, PreparadorDeCherryPick preparador, CherryPickEmLote cherry,
        StashDeCommitsEmLote stash, ServicoDaFila fila, CoordenadorDeOperacoes coordenador, IServicoDeJanelas janelas, SugestoesDeBranches sugestoes)
    {
        (_leitor, _preparador, _cherry, _stash, _fila, _coordenador, _janelas, _sugestoes) = (leitor, preparador, cherry, stash, fila, coordenador, janelas, sugestoes);
        Paineis = [.. projetos.Marcados.Select(p => Criar(p, detalhes))];
        PainelAtual = Paineis.Count > 0 ? Paineis[0] : null;
        _debounce.Tick += (_, _) => { _debounce.Stop(); Filtrar(); };
        _coordenador.Alterado += AoMudarOperacao;
        _fila.Mudou += AoMudarFila;
        AoMudarFila();
    }

    public ObservableCollection<string> Sugestoes { get; } = [];

    public IReadOnlyList<PainelDeHistorico> Paineis { get; }

    /// <summary>Aviso curto para o canto da janela (título, texto, grave).</summary>
    public event Action<string, string, bool>? Aviso;

    public bool PodeAgir => TotalMarcados > 0 && !Ocupado;

    public string TextoCherry => $"Cherry-pick ({TotalMarcados})";

    public string TextoStash => $"Stash ({TotalMarcados})";

    public string TextoFila => $"Adicionar à fila ({TotalMarcados})";

    public bool AbasVisiveis => !LadoALado && Paineis.Count > 1;

    public async Task CarregarAsync()
    {
        _ = CarregarSugestoesAsync();
        await Task.WhenAll(Paineis.Select(p => p.CarregarAsync(Escopo, MostrarMerges)));
        Filtrar();
    }

    public bool Visivel(PainelDeHistorico painel) => LadoALado || painel == PainelAtual;

    public void AlternarLayout() => LadoALado = !LadoALado;

    public void LimparFiltros() => (Texto, Autor, De, Ate) = ("", "", null, null);

    public async Task AplicarAgoraAsync()
    {
        var planos = await MontarPlanosAsync();
        if (planos.Count == 0) return;
        if (await _coordenador.ExecutarAsync(_cherry, new OpcoesDeCherryPick(planos, Destino.Trim())) is null) Avisar("Cherry-pick", "Há outra operação em andamento.", true);
    }

    public async Task GuardarEmStashAsync()
    {
        var planos = await MontarPlanosAsync();
        if (planos.Count == 0) return;
        if (await _coordenador.ExecutarAsync(_stash, new OpcoesDeStashDeCommits(planos)) is null) Avisar("Stash", "Há outra operação em andamento.", true);
    }

    public async Task AdicionarAFilaAsync()
    {
        var novos = new List<ItemDaFila>();
        foreach (var painel in Paineis)
            foreach (var commit in painel.Marcados)
                novos.Add(LeitorDeDetalhes.ParaItem(painel.Projeto, commit, await _leitor.OndeEstaAsync(painel.Projeto.Caminho, commit.Hash)));

        if (novos.Count == 0) return;
        var entrou = _fila.Adicionar(novos);
        if (entrou < 0) Avisar("Fila de cherry-pick", "A fila está rodando: espere terminar ou pare a fila.", true);
        else
        {
            foreach (var painel in Paineis) painel.DesmarcarTudo();
            Avisar("Fila de cherry-pick", entrou == novos.Count ? $"{entrou} commit(s) na fila." : $"{entrou} commit(s) na fila; {novos.Count - entrou} já estavam nela.", false);
        }
    }

    public void AbrirFila() => _janelas.Abrir(IdDaFila);

    public void Dispose()
    {
        _coordenador.Alterado -= AoMudarOperacao;
        _fila.Mudou -= AoMudarFila;
        foreach (var painel in Paineis) painel.Dispose();
        _debounce.Stop();
    }

    partial void OnTextoChanged(string value) => Reagendar();

    partial void OnAutorChanged(string value) => Reagendar();

    partial void OnDeChanged(DateTime? value) => Reagendar();

    partial void OnAteChanged(DateTime? value) => Reagendar();

    partial void OnEscopoChanged(string value) => _ = RecarregarAsync();

    partial void OnMostrarMergesChanged(bool value) => _ = RecarregarAsync();

    partial void OnLadoALadoChanged(bool value)
    {
        OnPropertyChanged(nameof(AbasVisiveis));
        AtualizarVisibilidade();
    }

    partial void OnPainelAtualChanged(PainelDeHistorico? value) => AtualizarVisibilidade();

    void AtualizarVisibilidade()
    {
        foreach (var p in Paineis) p.Visivel = LadoALado || p == PainelAtual;
    }

    PainelDeHistorico Criar(Projeto projeto, LeitorDeDetalhes detalhes)
    {
        var painel = new PainelDeHistorico(projeto, _leitor, detalhes);
        painel.MarcacaoMudou += () => TotalMarcados = Paineis.Sum(p => p.QuantidadeMarcados);
        return painel;
    }

    async Task RecarregarAsync()
    {
        await Task.WhenAll(Paineis.Select(p => p.CarregarAsync(Escopo, MostrarMerges)));
        TotalMarcados = Paineis.Sum(p => p.QuantidadeMarcados);
    }

    void Reagendar()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    void Filtrar()
    {
        var filtro = new FiltroHistorico(Texto, Autor, De, Ate);
        foreach (var painel in Paineis) painel.Aplicar(filtro);
    }

    async Task<IReadOnlyList<PlanoDeCherryPick>> MontarPlanosAsync()
    {
        var planos = new List<PlanoDeCherryPick>();
        foreach (var painel in Paineis.Where(p => p.QuantidadeMarcados > 0))
            planos.Add(await _preparador.DeCommitsAsync(painel.Projeto, painel.Marcados, Destino));
        return planos;
    }

    async Task CarregarSugestoesAsync()
    {
        var nomes = await _sugestoes.BasesAsync(Paineis.Select(p => p.Projeto));
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var n in nomes) Sugestoes.Add(n);
        });
    }

    void AoMudarOperacao() => System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Ocupado = _coordenador.EmAndamento);

    void AoMudarFila() => System.Windows.Application.Current.Dispatcher.InvokeAsync(() => NaFila = _fila.Fila.Pendentes);

    void Avisar(string titulo, string texto, bool grave) => Aviso?.Invoke(titulo, texto, grave);
}
