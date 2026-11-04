namespace Menage.Core.Repos;

/// <summary>Leitura da saída de texto do git, linha a linha.</summary>
public static class SaidaGit
{
    public static IReadOnlyList<string> Linhas(this SaidaProcesso saida) =>
        [.. saida.Texto.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0)];

    public static string PrimeiraLinha(this SaidaProcesso saida) => saida.Linhas() is { Count: > 0 } linhas ? linhas[0] : "";
}
