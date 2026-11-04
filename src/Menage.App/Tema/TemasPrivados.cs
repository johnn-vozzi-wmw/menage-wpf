using System.Windows.Media;
using Menage.Core;
using Menage.Core.Armazenamento;

namespace Menage.App.Tema;

/// <summary>
/// Tema de fora do programa: só o matiz e o destaque; o resto é derivado da paleta escura ou clara.
/// Cores em "#RRGGBB". O par pode ser outro privado do mesmo arquivo ou um tema do programa.
/// </summary>
public sealed record TemaPrivado(
    string Id, string Nome, string Par, bool Escuro, string Matiz, string Destaque, string DestaqueHover, string Azul, string? Emblema = null)
{
    /// <summary>Nulo se faltar id ou nome ou se alguma cor for inválida.</summary>
    public DefinicaoDeTema? Montar()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Nome)) return null;
        if (Cor(Matiz) is not { } matiz || Cor(Destaque) is not { } destaque ||
            Cor(DestaqueHover) is not { } hover || Cor(Azul) is not { } azul) return null;

        var baseDaPaleta = Escuro ? PaletasBase.Escura : PaletasBase.Clara;
        var cores = DerivacaoDePaleta.Derivar(baseDaPaleta, Escuro, matiz, destaque, hover, azul);
        return new DefinicaoDeTema(Id, Nome, Par, Escuro, DerivacaoDePaleta.Finalizar(cores, Escuro), Emblema, Privado: true);
    }

    static Color? Cor(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#') return null;
        return int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb)
            ? Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : null;
    }
}

/// <summary>Conteúdo de <c>temas-privados.json</c>, na pasta de dados do usuário.</summary>
public sealed record ArquivoDeTemasPrivados(IReadOnlyList<TemaPrivado> Temas);

/// <summary>Soma ao catálogo os temas do arquivo. Sem arquivo, não há privados e nada acontece.</summary>
public sealed class CarregadorDeTemasPrivados(IArmazenamento<ArquivoDeTemasPrivados> armazenamento, ICatalogoDeTemas catalogo)
{
    public Resultado<IReadOnlyList<string>> Carregar() =>
        armazenamento.Carregar().Mapear(arquivo => catalogo.DefinirPrivados(arquivo.Temas));
}
