using Menage.Core;
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Tema;
using Menage.Core.Comandos;
using Menage.Core.Personalizacao;

namespace Menage.App.Modulos.Personalizacao;

public sealed record AmostraDeCor(string Hex, Brush Pincel);

/// <summary>Um tema do programa visto como "família": o par escuro/claro com o mesmo nome.</summary>
public sealed record FamiliaDeTema(string IdEscuro, string IdClaro, string Nome);

public sealed record LinhaDeAtalho(string Id, string Texto, string Grupo, string? Atalho, bool Personalizado)
{
    public string AtalhoTexto => Atalho ?? "—";
}

/// <summary>
/// Estado da janela de Personalização. Cada opção vale na hora (grava pelo <see cref="ServicoDeAparencia"/>);
/// não há botão Salvar. Atalhos: o gesto capturado é validado pelo <see cref="MapaDeAtalhos"/> (conflito, tecla sozinha).
/// </summary>
public sealed partial class JanelaDePersonalizacaoViewModel : ObservableObject
{
    static readonly string[] _cores = ["#3574F0", "#0096C7", "#00A878", "#7C3AED", "#D6336C", "#E8590C", "#F59F00", "#6C757D"];

    readonly ServicoDeAparencia _aparencia;
    readonly IServicoDeTema _tema;
    readonly ServicoDeAtalhos _atalhos;
    readonly ICatalogoDeComandos _comandos;


    [ObservableProperty]
    LinhaDeAtalho? _selecionada;

    [ObservableProperty]
    string _filtroDeAtalhos = "";

    [ObservableProperty]
    string _mensagemDeAtalho = "";

    [ObservableProperty]
    string _campoDeCor = "";

    [ObservableProperty]
    string _mensagemDeCor = "";

    public JanelaDePersonalizacaoViewModel(
        ServicoDeAparencia aparencia, IPreferenciasDeAparencia preferencias, IServicoDeTema tema, ICatalogoDeTemas catalogo,
        ServicoDeAtalhos atalhos, ICatalogoDeComandos comandos)
    {
        (_aparencia, _tema, _atalhos, _comandos) = (aparencia, tema, atalhos, comandos);
        Familias = [.. catalogo.Todos.Where(t => t.Escuro).Select(t => new FamiliaDeTema(t.Id, t.Par, t.Nome.Replace(" escuro", "", StringComparison.OrdinalIgnoreCase)))];
        Amostras = [.. _cores.Select(h => new AmostraDeCor(h, Pincel(h)))];
        preferencias.Alterado += _ => Atualizar();
        CampoDeCor = Preferencias.CorDeDestaque ?? "";
        Reconstruir();
    }

    public IReadOnlyList<FamiliaDeTema> Familias { get; }

    public IReadOnlyList<AmostraDeCor> Amostras { get; }

    public ObservableCollection<LinhaDeAtalho> Atalhos { get; } = [];

    PreferenciasDeAparencia Preferencias => _aparencia.Atual;

    public ModoDeTema Modo
    {
        get => Preferencias.Modo ?? (_tema.Escolhido.Escuro ? ModoDeTema.Escuro : ModoDeTema.Claro);
        set => _aparencia.Alterar(p => p with { Modo = value });
    }

    public FamiliaDeTema? Familia
    {
        get => Familias.FirstOrDefault(f => f.IdEscuro == _tema.Escolhido.Id || f.IdClaro == _tema.Escolhido.Id);
        set
        {
            if (value is null) return;
            _tema.Trocar(_tema.Escolhido.Escuro ? value.IdEscuro : value.IdClaro);
            OnPropertyChanged(nameof(Modo));
        }
    }

    public Densidade Densidade
    {
        get => Preferencias.Densidade;
        set => _aparencia.Alterar(p => p with { Densidade = value });
    }

    public TamanhoDeFonte Fonte
    {
        get => Preferencias.Fonte;
        set => _aparencia.Alterar(p => p with { Fonte = value });
    }

    public ModeloDaBarra Barra
    {
        get => Preferencias.Barra;
        set => _aparencia.Alterar(p => p with { Barra = value });
    }

    public bool ReduzirMovimento
    {
        get => Preferencias.ReduzirMovimento;
        set => _aparencia.Alterar(p => p with { ReduzirMovimento = value });
    }

    public string DestaqueAtual => Preferencias.CorDeDestaque ?? "padrão do tema";

    [RelayCommand]
    void EscolherCor(string hex)
    {
        _aparencia.Alterar(p => p with { CorDeDestaque = hex });
        CampoDeCor = hex;
        MensagemDeCor = "";
    }

    [RelayCommand]
    void AplicarCampoDeCor()
    {
        if (string.IsNullOrWhiteSpace(CampoDeCor)) { CorPadrao(); return; }
        if (MedidasDeAparencia.NormalizarCor(CampoDeCor) is not { } cor)
        {
            MensagemDeCor = "Use o formato #RRGGBB (por exemplo #7C3AED).";
            return;
        }

        EscolherCor(cor);
    }

    [RelayCommand]
    void CorPadrao()
    {
        _aparencia.Alterar(p => p with { CorDeDestaque = null });
        CampoDeCor = "";
        MensagemDeCor = "";
    }

    // ---- atalhos ----

    partial void OnFiltroDeAtalhosChanged(string value) => Reconstruir();

    /// <summary>Chamado pela tela com o gesto digitado no campo de captura.</summary>
    public void DefinirAtalho(Gesto gesto)
    {
        if (Selecionada is null) { MensagemDeAtalho = "Escolha um comando na lista."; return; }
        Gravar(m => m.Definir(Selecionada.Id, gesto.ToString()), $"{Selecionada.Texto}: {gesto}");
    }

    [RelayCommand]
    void RemoverAtalho()
    {
        if (Selecionada is null) return;
        Gravar(m => m.Definir(Selecionada.Id, ""), $"{Selecionada.Texto}: sem atalho");
    }

    [RelayCommand]
    void RestaurarAtalho()
    {
        if (Selecionada is null) return;
        Gravar(m => { m.Restaurar(Selecionada.Id); return Resultado.Ok(); }, $"{Selecionada.Texto}: atalho padrão");
    }

    [RelayCommand]
    void RestaurarTodosOsAtalhos() => Gravar(m => { m.RestaurarTodos(); return Resultado.Ok(); }, "Todos os atalhos voltaram ao padrão.");

    void Gravar(Func<MapaDeAtalhos, Resultado<Nada>> mudanca, string aviso)
    {
        var mapa = _atalhos.Montar();
        var r = mudanca(mapa);
        if (!r.EhOk)
        {
            MensagemDeAtalho = r.Erro.ToString();
            return;
        }

        _aparencia.Alterar(p => p with { Atalhos = new Dictionary<string, string>(mapa.Trocas) });
        MensagemDeAtalho = aviso;
    }

    void Atualizar()
    {
        OnPropertyChanged(string.Empty);
        Reconstruir();
    }

    void Reconstruir()
    {
        var mapa = _atalhos.Mapa;
        var anterior = Selecionada?.Id;
        var termos = FiltroDeAtalhos.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Atalhos.Clear();
        foreach (var c in _comandos.Todos.OrderBy(c => c.Grupo, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Texto, StringComparer.CurrentCultureIgnoreCase))
        {
            var linha = new LinhaDeAtalho(c.Id, c.Texto, c.Grupo, mapa.Efetivo(c.Id), mapa.Personalizado(c.Id));
            if (termos.All(t => $"{c.Texto} {c.Grupo} {linha.Atalho}".Contains(t, StringComparison.CurrentCultureIgnoreCase))) Atalhos.Add(linha);
        }

        Selecionada = Atalhos.FirstOrDefault(l => l.Id == anterior);
    }

    static SolidColorBrush Pincel(string hex)
    {
        var cor = AjusteDeDestaque.Ler(hex)!.Value;
        var pincel = new SolidColorBrush(cor);
        pincel.Freeze();
        return pincel;
    }
}
