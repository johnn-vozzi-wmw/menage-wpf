namespace Menage.App.Controles;

public enum ModoDaBarra { Capsula, SoIcone, Linhas }

/// <summary>Posição de uma pílula na barra.</summary>
public readonly record struct PosicaoNaBarra(double X, double Y, double Largura, int Linha);

public sealed record LayoutDaBarra(ModoDaBarra Modo, IReadOnlyList<PosicaoNaBarra> Posicoes, double Largura, double Altura);

/// <summary>
/// Regra responsiva da barra, sem WPF: cabe com texto (cápsula) → cabe só com ícones → quebra em linhas.
/// Numa linha única, a largura que sobra é dividida entre as pílulas (sem buraco antes da última).
/// </summary>
public static class CalculoDaBarra
{
    public static LayoutDaBarra Calcular(
        IReadOnlyList<double> completas, IReadOnlyList<double> compactas, double disponivel, double alturaDoItem, double espaco)
    {
        if (double.IsInfinity(disponivel)) return EmLinha(ModoDaBarra.Capsula, completas, Total(completas, espaco), alturaDoItem, espaco);
        if (Total(completas, espaco) <= disponivel) return EmLinha(ModoDaBarra.Capsula, completas, disponivel, alturaDoItem, espaco);
        if (Total(compactas, espaco) <= disponivel) return EmLinha(ModoDaBarra.SoIcone, compactas, disponivel, alturaDoItem, espaco);
        return EmLinhas(compactas, disponivel, alturaDoItem, espaco);
    }

    static double Total(IReadOnlyList<double> larguras, double espaco) =>
        larguras.Sum() + (espaco * Math.Max(0, larguras.Count - 1));

    static LayoutDaBarra EmLinha(ModoDaBarra modo, IReadOnlyList<double> larguras, double disponivel, double altura, double espaco)
    {
        var sobra = larguras.Count == 0 ? 0 : (disponivel - Total(larguras, espaco)) / larguras.Count;
        var posicoes = new List<PosicaoNaBarra>(larguras.Count);
        var x = 0.0;
        foreach (var largura in larguras)
        {
            posicoes.Add(new PosicaoNaBarra(x, 0, largura + sobra, 0));
            x += largura + sobra + espaco;
        }

        return new LayoutDaBarra(modo, posicoes, disponivel, larguras.Count == 0 ? 0 : altura);
    }

    static LayoutDaBarra EmLinhas(IReadOnlyList<double> larguras, double disponivel, double altura, double espaco)
    {
        var posicoes = new List<PosicaoNaBarra>(larguras.Count);
        var (x, linha) = (0.0, 0);
        foreach (var largura in larguras)
        {
            if (x > 0 && x + largura > disponivel) (x, linha) = (0, linha + 1);
            posicoes.Add(new PosicaoNaBarra(x, linha * (altura + espaco), largura, linha));
            x += largura + espaco;
        }

        return new LayoutDaBarra(ModoDaBarra.Linhas, posicoes, disponivel, ((linha + 1) * altura) + (linha * espaco));
    }
}
