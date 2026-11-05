using System.Text.RegularExpressions;

namespace Menage.Core.Operacoes.Commit;

/// <summary>Sugere o prefixo "VENDAS-123: " a partir do nome da branch (ex.: <c>feature/VENDAS-123-tela</c>).</summary>
public static partial class SugestaoDeTicket
{
    public static string? DaBranch(string branch)
    {
        var achado = ChaveDeTicket().Match(branch ?? "");
        return achado.Success ? achado.Value.ToUpperInvariant() + ": " : null;
    }

    /// <summary>A sugestão mais repetida entre as branches; nulo se nenhuma tem ticket.</summary>
    public static string? DasBranches(IEnumerable<string> branches) =>
        branches.Select(DaBranch).OfType<string>().GroupBy(s => s).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z][A-Za-z0-9]*-\d+", RegexOptions.CultureInvariant)]
    private static partial Regex ChaveDeTicket();
}
