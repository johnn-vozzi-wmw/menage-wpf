using System.Text.RegularExpressions;

namespace Menage.Core.BetaAutomatica;

public sealed record TrechoDeJql(string Texto, bool Marcador);

/// <summary>Separa o texto de uma JQL em trechos comuns e marcadores <c>{projeto}</c>, para a tela destacar os marcadores.</summary>
public static partial class MarcadoresDeJql
{
    [GeneratedRegex(@"(\{[A-Za-z_]+\})")]
    private static partial Regex Padrao();

    public static IReadOnlyList<TrechoDeJql> Separar(string? jql) =>
        [.. Padrao().Split(jql ?? "").Where(p => p.Length > 0).Select(p => new TrechoDeJql(p, p.Length > 2 && p[0] == '{' && p[^1] == '}'))];
}
