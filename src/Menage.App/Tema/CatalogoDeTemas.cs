using System.Windows.Media;
using static Menage.App.Tema.DerivacaoDePaleta;

namespace Menage.App.Tema;

public interface ICatalogoDeTemas
{
    /// <summary>Temas do programa seguidos dos privados carregados.</summary>
    IReadOnlyList<DefinicaoDeTema> Todos { get; }

    DefinicaoDeTema? Buscar(string id);

    /// <summary>O par claro/escuro; sem par válido, o Claro ou o Escuro do programa.</summary>
    DefinicaoDeTema ParDe(DefinicaoDeTema tema);

    /// <summary>O tema pedido; se não existe (privado sem acesso), o Escuro.</summary>
    DefinicaoDeTema BuscarOuPadrao(string? id);

    /// <summary>Troca os temas privados. Entradas inválidas ficam de fora e voltam na lista de rejeitadas.</summary>
    IReadOnlyList<string> DefinirPrivados(IEnumerable<TemaPrivado> privados);
}

/// <summary>Os 12 temas do programa (6 pares), mais os privados que vierem de arquivo.</summary>
public sealed class CatalogoDeTemas : ICatalogoDeTemas
{
    public const string IdEscuro = "escuro";
    public const string IdClaro = "claro";

    readonly IReadOnlyList<DefinicaoDeTema> _base = MontarBase();
    IReadOnlyList<DefinicaoDeTema> _privados = [];

    public IReadOnlyList<DefinicaoDeTema> Todos => [.. _base, .. _privados];

    public DefinicaoDeTema? Buscar(string id) => Todos.FirstOrDefault(t => t.Id == id);

    public DefinicaoDeTema BuscarOuPadrao(string? id) => (id is null ? null : Buscar(id)) ?? Buscar(IdEscuro)!;

    public DefinicaoDeTema ParDe(DefinicaoDeTema tema) => Buscar(tema.Par) ?? Buscar(tema.Escuro ? IdClaro : IdEscuro)!;

    public IReadOnlyList<string> DefinirPrivados(IEnumerable<TemaPrivado> privados)
    {
        var aceitos = new List<DefinicaoDeTema>();
        var rejeitados = new List<string>();
        foreach (var privado in privados)
        {
            var repetido = _base.Concat(aceitos).Any(t => t.Id == privado.Id);
            var tema = repetido ? null : privado.Montar();
            if (tema is null) rejeitados.Add(privado.Id);
            else aceitos.Add(tema);
        }

        _privados = aceitos;
        return rejeitados;
    }

    static DefinicaoDeTema[] MontarBase() =>
    [
        Novo("escuro", "Escuro", "claro", true, PaletasBase.Escura),
        Novo("claro", "Claro", "escuro", false, PaletasBase.Clara),
        Novo("azul-escuro", "Azul escuro", "azul-claro", true, PaletasBase.AzulEscura),
        Novo("azul-claro", "Azul claro", "azul-escuro", false, PaletasBase.AzulClara),
        Tingido("esmeralda-escuro", "Esmeralda escuro", "esmeralda-claro", true, (0, 62, 52), (0, 150, 118), (0, 172, 136), (70, 214, 182)),
        Tingido("esmeralda-claro", "Esmeralda claro", "esmeralda-escuro", false, (206, 240, 230), (0, 130, 102), (0, 150, 118), (0, 118, 96)),
        Tingido("ameixa-escuro", "Ameixa escuro", "ameixa-claro", true, (48, 22, 82), (124, 78, 236), (146, 104, 248), (184, 156, 255)),
        Tingido("ameixa-claro", "Ameixa claro", "ameixa-escuro", false, (234, 224, 250), (108, 60, 210), (126, 82, 226), (98, 58, 190)),
        Tingido("grafite-escuro", "Grafite escuro", "grafite-claro", true, (8, 8, 8), (232, 120, 40), (244, 140, 64), (255, 176, 110)),
        Tingido("grafite-claro", "Grafite claro", "grafite-escuro", false, (236, 236, 236), (206, 96, 20), (222, 114, 36), (180, 84, 16)),
        // Destaque no verde; detalhes (informação, grupo WEB) no amarelo.
        Tingido("verde-amarelo-escuro", "Nacional escuro", "verde-amarelo-claro", true, (0, 52, 26), (0, 150, 60), (0, 172, 72), (255, 214, 0), "bandeira"),
        Tingido("verde-amarelo-claro", "Nacional claro", "verde-amarelo-escuro", false, (226, 244, 222), (0, 140, 56), (0, 160, 66), (176, 136, 0), "bandeira"),
    ];

    static DefinicaoDeTema Novo(string id, string nome, string par, bool escuro, IReadOnlyList<Color> cores, string? emblema = null) =>
        new(id, nome, par, escuro, Finalizar(cores, escuro), emblema);

    static DefinicaoDeTema Tingido(string id, string nome, string par, bool escuro,
        (byte, byte, byte) matiz, (byte, byte, byte) destaque, (byte, byte, byte) hover, (byte, byte, byte) azul, string? emblema = null)
    {
        var baseDaPaleta = escuro ? PaletasBase.Escura : PaletasBase.Clara;
        return Novo(id, nome, par, escuro, Derivar(baseDaPaleta, escuro, Rgb(matiz), Rgb(destaque), Rgb(hover), Rgb(azul)), emblema);
    }

    static Color Rgb((byte R, byte G, byte B) c) => Color.FromRgb(c.R, c.G, c.B);
}
