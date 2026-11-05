using System.Globalization;

namespace Menage.Core.Personalizacao;

/// <summary>Valores calculados a partir das preferências: a tela só os põe nos recursos. Sem WPF, testável.</summary>
public static class MedidasDeAparencia
{
    public static double Escala(TamanhoDeFonte tamanho) => tamanho switch
    {
        TamanhoDeFonte.Pequeno => 0.9,
        TamanhoDeFonte.Grande => 1.15,
        _ => 1.0,
    };

    /// <summary>Tamanho de fonte escalado, em passos de 0,5 para o texto não ficar borrado.</summary>
    public static double Fonte(double basePx, TamanhoDeFonte tamanho) =>
        Math.Round(basePx * Escala(tamanho) * 2, MidpointRounding.AwayFromZero) / 2;

    public static double AlturaDoControle(Densidade d) => d == Densidade.Compacta ? 26 : 32;

    /// <summary>Altura mínima da linha das grades; cresce junto com a fonte para o texto não ser cortado.</summary>
    public static double AlturaDaLinha(Densidade d, TamanhoDeFonte tamanho) =>
        Math.Round((d == Densidade.Compacta ? 26 : 32) * Math.Max(1, Escala(tamanho)));

    public static double MargemDoCartao(Densidade d) => d == Densidade.Compacta ? 8 : 14;

    /// <summary>"#RRGGBB" (com ou sem #, 3 ou 6 dígitos) em maiúsculas; nulo se inválido.</summary>
    public static string? NormalizarCor(string? texto)
    {
        var t = (texto ?? "").Trim().TrimStart('#');
        if (t.Length == 3) t = string.Concat(t.Select(c => $"{c}{c}"));
        return t.Length == 6 && int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) ? "#" + t.ToUpperInvariant() : null;
    }

    /// <summary>Cor de passar o mouse: mistura 18% com branco se a base é escura, com preto se é clara.</summary>
    public static (byte R, byte G, byte B) Hover((byte R, byte G, byte B) cor)
    {
        var luz = (0.299 * cor.R + 0.587 * cor.G + 0.114 * cor.B) / 255;
        var alvo = luz < 0.6 ? 255 : 0;
        byte Mistura(byte c) => (byte)Math.Round(c + (alvo - c) * 0.18);
        return (Mistura(cor.R), Mistura(cor.G), Mistura(cor.B));
    }

    /// <summary>Qual lado do par de temas vale: Sistema segue o Windows; sem escolha (nulo) mantém o lado do tema salvo.</summary>
    public static bool UsaEscuro(ModoDeTema? modo, bool sistemaEscuro, bool temaSalvoEscuro) => modo switch
    {
        ModoDeTema.Claro => false,
        ModoDeTema.Escuro => true,
        ModoDeTema.Sistema => sistemaEscuro,
        _ => temaSalvoEscuro,
    };
}
