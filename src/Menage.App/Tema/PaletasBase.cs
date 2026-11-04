using System.Windows.Media;

namespace Menage.App.Tema;

/// <summary>
/// Paletas de base, na ordem de <see cref="PapelDeCor"/>. Valores vindos do Git Manager antigo
/// (linhas: superfícies, textos, destaque e estados, botões e grade, desabilitados, grupos, console).
/// </summary>
public static class PaletasBase
{
    public static IReadOnlyList<Color> Escura { get; } = Ler(
    [
        "#1E1F22", "#2B2D30", "#36393D", "#252629", "#43464A",
        "#DFE1E5", "#8C8F94", "#FAFBFC",
        "#3574F0", "#4B86F7", "#5FB878", "#E6B450", "#E68C46", "#E85C5C", "#6EA8FE",
        "#2C2E32", "#26282B", "#42464B", "#2E3034", "#323A4B", "#3C3F44", "#34363A", "#2E436E",
        "#282A2D", "#5F6267", "#505358",
        "#6EA8FD", "#B48CFA", "#5FC8A0", "#F0A06E", "#313439",
        "#17181A", "#D6D9DE", "#242629", "#34373C",
    ]);

    public static IReadOnlyList<Color> Clara { get; } = Ler(
    [
        "#F3F4F7", "#FEFEFE", "#E1E5EC", "#E8EBF0", "#CDD1D8",
        "#24272D", "#696F79", "#0F1218",
        "#2563EB", "#3B76F5", "#1E8C46", "#B07600", "#C85C14", "#CD3232", "#1E64D2",
        "#F6F7FA", "#E2E5EB", "#D2D8E2", "#DBE0E8", "#D0DEF8", "#D6DBE3", "#E4E7EC", "#CFE0FC",
        "#ECEEF2", "#A5AAB2", "#BEC3CB",
        "#1E64D3", "#7846C8", "#148C64", "#C86428", "#EDF1F8",
        "#17181A", "#D6D9DE", "#242629", "#34373C",
    ]);

    public static IReadOnlyList<Color> AzulEscura { get; } = Ler(
    [
        "#0D1421", "#141E30", "#1E2B42", "#0F1828", "#2C3C56",
        "#DCE6F2", "#8092AC", "#F8FBFF",
        "#006AFF", "#128AFF", "#50C88C", "#EBB950", "#EB9146", "#EE6064", "#3CC8FF",
        "#182439", "#162134", "#263754", "#1A2840", "#0A3C70", "#243450", "#1E2C42", "#08407A",
        "#1A2436", "#54647C", "#42526C",
        "#3CC8FE", "#968CFF", "#46D7BE", "#F5A56E", "#1A273D",
        "#0A101C", "#D6DEEA", "#162032", "#223048",
    ]);

    public static IReadOnlyList<Color> AzulClara { get; } = Ler(
    [
        "#F0F5FB", "#FEFEFE", "#DEE9F6", "#E5EEFA", "#C6D5E8",
        "#182438", "#5E6E86", "#081224",
        "#0062FF", "#0080FF", "#198C50", "#B07600", "#C85C14", "#CD323C", "#0070E6",
        "#F5F9FE", "#DFE9F7", "#CDDDF3", "#D5E4F8", "#C8E9FD", "#CFDEF2", "#E1EAF5", "#C8E6FF",
        "#E9EFF7", "#9EABBE", "#B8C5D7",
        "#0070E7", "#6E50DC", "#00918C", "#C86428", "#E9F3FE",
        "#0A101C", "#D6DEEA", "#162032", "#223048",
    ]);

    static Color[] Ler(string[] hex) => [.. hex.Select(h => (Color)ColorConverter.ConvertFromString(h))];
}
