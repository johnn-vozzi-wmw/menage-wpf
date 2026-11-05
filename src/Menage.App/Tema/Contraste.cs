using System.Windows.Media;

namespace Menage.App.Tema;

/// <summary>Razão de contraste WCAG 2.x entre duas cores (1 a 21). Texto normal pede 4,5; texto grande e controles, 3.</summary>
public static class Contraste
{
    public const double TextoNormal = 4.5;
    public const double TextoGrandeOuControle = 3.0;

    public static double Razao(Color a, Color b)
    {
        var (maior, menor) = (Luminancia(a), Luminancia(b));
        if (maior < menor) (maior, menor) = (menor, maior);
        return (maior + 0.05) / (menor + 0.05);
    }

    public static double Luminancia(Color cor) =>
        0.2126 * Linear(cor.R) + 0.7152 * Linear(cor.G) + 0.0722 * Linear(cor.B);

    static double Linear(byte canal)
    {
        var c = canal / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
