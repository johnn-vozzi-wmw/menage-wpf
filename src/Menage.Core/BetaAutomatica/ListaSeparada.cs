namespace Menage.Core.BetaAutomatica;

/// <summary>Campos de lista da configuração (chaves do Jira, status finais, tipos de subtask) guardados como texto separado por vírgula.</summary>
public static class ListaSeparada
{
    static readonly char[] Virgulas = [',', ';'];
    static readonly char[] VirgulasEEspaco = [',', ';', ' '];

    public static List<string> Ler(string? texto, bool separarPorEspaco = false) =>
        [.. (texto ?? "").Split(separarPorEspaco ? VirgulasEEspaco : Virgulas, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    public static string Juntar(IEnumerable<string> itens) =>
        string.Join(",", itens.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
}
