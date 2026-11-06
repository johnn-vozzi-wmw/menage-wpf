using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Icones;
using Menage.App.Tema;
using Menage.Core.Personalizacao;

namespace Menage.App.Modulos.Personalizacao;

/// <summary>Um tema na coluna 1 da aba Ícones. <see cref="TemIcone"/> acende o ponto verde (ícone próprio, não o geral).</summary>
public sealed partial class ChipDeTema(string id, string nome, bool temIcone) : ObservableObject
{
    public string Id { get; } = id;

    public string Nome { get; } = nome;

    [ObservableProperty]
    bool _temIcone = temIcone;
}

/// <summary>
/// Aba Ícones da Personalização, em três passos: 1) tema, 2) imagem/GIF com zoom, 3) onde aparece (com maquetes nas cores do tema).
/// Cada escolha vale na hora (grava em aparencia.json). O arquivo é validado (extensão e decodificação) pelo mesmo cache que
/// mostra o ícone depois; o seletor de arquivo fica atrás de <see cref="ISeletorDeImagem"/>.
/// </summary>
public sealed partial class IconesViewModel : ObservableObject
{
    const string SemIcone = "Ícone padrão";

    readonly IPreferenciasDeAparencia _preferencias;
    readonly ServicoDeIcones _servico;
    readonly ISeletorDeImagem _seletor;
    readonly ICatalogoDeTemas _catalogo;

    [ObservableProperty]
    string _mensagem = "";

    [ObservableProperty]
    ChipDeTema _temaSelecionado;

    public IconesViewModel(
        IPreferenciasDeAparencia preferencias, ServicoDeIcones servico, ICatalogoDeTemas temas, Menage.Core.Comandos.ICatalogoDeComandos comandos,
        Menage.Core.Projetos.IConfiguracaoDeProjetos configuracao, ISeletorDeImagem seletor)
    {
        (_preferencias, _servico, _seletor, _catalogo) = (preferencias, servico, seletor, temas);
        var proprios = preferencias.Valor.Icones.PorTema;
        Temas = [new(IconesPersonalizados.TodosOsTemas, "Todos os temas (padrão)", Tem(proprios, IconesPersonalizados.TodosOsTemas)),
            .. temas.Todos.Select(t => new ChipDeTema(t.Id, t.Nome, Tem(proprios, t.Id)))];
        _temaSelecionado = Temas.FirstOrDefault(t => t.Id == temas.BuscarOuPadrao(null).Id) ?? Temas[0];
        CarregarPorItem(comandos, configuracao);
    }

    public IReadOnlyList<ChipDeTema> Temas { get; }

    IconesPersonalizados Icones => _preferencias.Valor.Icones;

    string Id => TemaSelecionado.Id;

    /// <summary>Ícone do próprio tema escolhido (nulo se só vale o geral ou o padrão).</summary>
    public string? Caminho => Icones.PorTema.TryGetValue(Id, out var c) && c.Length > 0 ? c : null;

    public bool TemIcone => Caminho is not null;

    public bool PodeUsarEmTodos => TemIcone && Id != IconesPersonalizados.TodosOsTemas;

    public string NomeDoArquivo => Caminho is { } c ? Path.GetFileName(c)
        : Icones.OrigemDoApp(Id) is { } geral ? "Usa o ícone geral: " + Path.GetFileName(Icones.PorTema[geral]) : SemIcone;

    public string TextoDaZona => TemIcone ? "Trocar imagem" : "Escolher imagem";

    /// <summary>Ícone que vale para o tema (o próprio ou o geral), nulo se vale o padrão do Menage.</summary>
    string? Efetivo => Icones.DoApp(Id);

    public bool EhGif => Efetivo is { } c && IconesPersonalizados.EhGif(c);

    public bool UsaPadrao => Imagem is null;

    public bool AnimarGif => !_servico.ReduzirMovimento;

    public ImageSource? Imagem => Efetivo is { } c ? _servico.Imagem(c, ServicoDeIcones.LarguraGrande) : null;

    public ImageSource? ImagemEstatica => EhGif ? null : Imagem;

    public ImageSource? ImagemAnimada => EhGif ? Imagem : null;

    /// <summary>Zoom do ícone do tema, 50 a 200%; vale na hora e é gravado junto do ícone. Só com ícone próprio.</summary>
    public int ZoomPercentual
    {
        get => Icones.ZoomDoApp(Id);
        set
        {
            if (!TemIcone || value == ZoomPercentual) return;
            _preferencias.Alterar(p => p with { Icones = p.Icones.ComZoomDoApp(Id, value) });
            AvisarMudancas();
        }
    }

    public double ZoomFator => ZoomPercentual / 100.0;

    public string ZoomTexto => ZoomPercentual + "%";

    public bool AplicarNoMenuEPilulas
    {
        get => Icones.AplicarNoMenuEPilulas;
        set { _preferencias.Alterar(p => p with { Icones = p.Icones with { AplicarNoMenuEPilulas = value } }); OnPropertyChanged(); }
    }

    public bool AplicarEmGruposEProjetos
    {
        get => Icones.AplicarEmGruposEProjetos;
        set { _preferencias.Alterar(p => p with { Icones = p.Icones with { AplicarEmGruposEProjetos = value } }); OnPropertyChanged(); }
    }

    [RelayCommand]
    void EscolherImagem()
    {
        if (Selecionar() is { } caminho) Definir(caminho);
    }

    [RelayCommand]
    void Remover() => Definir(null);

    [RelayCommand]
    void UsarEmTodos()
    {
        if (!PodeUsarEmTodos) return;
        var origem = Id;
        _preferencias.Alterar(p => p with { Icones = p.Icones.UsarEmTodos(origem, _catalogo.Todos.Select(t => t.Id)) });
        AtualizarPontos();
    }

    /// <summary>Arquivo solto na zona de arrastar: vale como se tivesse sido escolhido no seletor.</summary>
    public void ReceberArquivo(string caminho)
    {
        if (Validar(caminho)) Definir(caminho);
    }

    partial void OnTemaSelecionadoChanged(ChipDeTema value) => AvisarMudancas();

    string? Selecionar()
    {
        string? escolhido;
        try { escolhido = _seletor.Escolher(); }
        catch (Exception ex) { Mensagem = "Não foi possível abrir a seleção de arquivo: " + ex.Message; return null; }
        return escolhido is { } caminho && Validar(caminho) ? caminho : null;
    }

    bool Validar(string caminho)
    {
        if (!IconesPersonalizados.ExtensaoValida(caminho) || _servico.Imagem(caminho) is null)
        {
            Mensagem = "Esse arquivo não é uma imagem válida (use PNG, JPG, BMP, ICO ou GIF).";
            return false;
        }

        Mensagem = "";
        return true;
    }

    void Definir(string? caminho)
    {
        _preferencias.Alterar(p => p with { Icones = p.Icones.ComIconeDoApp(Id, caminho) });
        TemaSelecionado.TemIcone = caminho is not null;
        AvisarMudancas();
    }

    void AtualizarPontos()
    {
        foreach (var chip in Temas) chip.TemIcone = Tem(Icones.PorTema, chip.Id);
        AvisarMudancas();
    }

    void AvisarMudancas()
    {
        foreach (var nome in new[]
        {
            nameof(Caminho), nameof(TemIcone), nameof(PodeUsarEmTodos), nameof(NomeDoArquivo), nameof(TextoDaZona), nameof(EhGif), nameof(UsaPadrao),
            nameof(Imagem), nameof(ImagemEstatica), nameof(ImagemAnimada), nameof(ZoomPercentual), nameof(ZoomFator), nameof(ZoomTexto),
            nameof(FundoDaMaquete), nameof(BarraDaMaquete), nameof(TextoDaMaquete), nameof(DestaqueDaMaquete),
        }) OnPropertyChanged(nome);
    }

    static bool Tem(Dictionary<string, string> mapa, string chave) => mapa.TryGetValue(chave, out var c) && c.Length > 0;

    // Cores das maquetes: as do tema escolhido (não as do tema em uso). "Todos os temas" mostra o Escuro.
    DefinicaoDeTema TemaDaMaquete => _catalogo.BuscarOuPadrao(Id == IconesPersonalizados.TodosOsTemas ? null : Id);

    public Brush FundoDaMaquete => Pincel(PapelDeCor.Fundo);

    public Brush BarraDaMaquete => Pincel(PapelDeCor.Painel);

    public Brush TextoDaMaquete => Pincel(PapelDeCor.Texto);

    public Brush DestaqueDaMaquete => Pincel(PapelDeCor.Destaque);

    SolidColorBrush Pincel(PapelDeCor papel)
    {
        var pincel = new SolidColorBrush(TemaDaMaquete[papel]);
        pincel.Freeze();
        return pincel;
    }
}
