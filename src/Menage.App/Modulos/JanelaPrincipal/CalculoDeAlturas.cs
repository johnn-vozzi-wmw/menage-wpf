namespace Menage.App.Modulos.JanelaPrincipal;

/// <summary>Quanto da altura livre fica com a grade e quanto com a Saída.</summary>
public readonly record struct AlturasDaTela(double Grade, double Saida);

/// <summary>
/// Regra de altura: a grade ocupa só as linhas visíveis (mais o cabeçalho) e a Saída fica com o resto, mas nunca com menos
/// do que o usuário arrastou (<c>preferida</c>) nem menos do mínimo; a grade nunca fica abaixo do mínimo e rola se faltar espaço.
/// Recolhida, a Saída é só o cabeçalho com a última linha e a grade fica com o resto.
/// </summary>
public static class CalculoDeAlturas
{
    public const double AlturaDaLinha = 32;
    public const double AlturaDoCabecalho = 34;
    public const double SaidaRecolhida = 32;
    public const double SaidaMinima = 96;
    public const double GradeMinima = AlturaDoCabecalho + (3 * AlturaDaLinha);
    public const double Divisoria = 8;

    public static AlturasDaTela Calcular(double disponivel, int linhasDaGrade, double preferida, bool recolhida)
    {
        var util = Math.Max(0, disponivel - Divisoria);
        if (recolhida) return new(Math.Max(0, util - SaidaRecolhida), Math.Min(SaidaRecolhida, util));

        var necessaria = AlturaDoCabecalho + (Math.Max(linhasDaGrade, 1) * AlturaDaLinha) + 2;
        var saida = Math.Max(Math.Max(preferida, SaidaMinima), util - necessaria);
        saida = Math.Min(saida, Math.Max(SaidaMinima, util - GradeMinima));
        saida = Math.Min(saida, util);
        return new(util - saida, saida);
    }

    /// <summary>Altura da Saída depois de arrastar a divisória por <paramref name="deslocamento"/> (positivo = para baixo).</summary>
    public static double AposArrastar(double alturaDaSaida, double deslocamento, double disponivel) =>
        Math.Clamp(alturaDaSaida - deslocamento, SaidaMinima, Math.Max(SaidaMinima, disponivel - Divisoria - GradeMinima));
}
