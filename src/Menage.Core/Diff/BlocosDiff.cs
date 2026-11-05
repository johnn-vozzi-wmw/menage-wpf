using DiffPlex;

namespace Menage.Core.Diff;

/// <summary>Bloco de diferença entre duas listas de linhas: [AIni, AIni + ALen) virou [BIni, BIni + BLen). Base 0.</summary>
public readonly record struct Bloco(int AIni, int ALen, int BIni, int BLen);

public static class BlocosDiff
{
    /// <summary>Corta o começo e o fim iguais antes do diff: é o que evita o pior caso em arquivos grandes.</summary>
    public static IReadOnlyList<Bloco> Entre(IReadOnlyList<string> a, IReadOnlyList<string> b, IDiffer? differ = null)
    {
        var limite = Math.Min(a.Count, b.Count);
        var prefixo = 0;
        while (prefixo < limite && string.Equals(a[prefixo], b[prefixo], StringComparison.Ordinal)) prefixo++;
        var sufixo = 0;
        while (sufixo < limite - prefixo && string.Equals(a[a.Count - 1 - sufixo], b[b.Count - 1 - sufixo], StringComparison.Ordinal)) sufixo++;

        var (meioA, meioB) = (Fatia(a, prefixo, sufixo), Fatia(b, prefixo, sufixo));
        if (meioA.Length == 0 && meioB.Length == 0) return [];
        if (meioA.Length == 0 || meioB.Length == 0) return [new(prefixo, meioA.Length, prefixo, meioB.Length)];

        var pedacos = new ChunkerFixo(meioA, meioB);
        var diff = (differ ?? new Differ()).CreateDiffs(ChunkerFixo.Antigo, ChunkerFixo.Novo, false, false, pedacos);
        return diff.DiffBlocks
            .Select(d => new Bloco(d.DeleteStartA + prefixo, d.DeleteCountA, d.InsertStartB + prefixo, d.InsertCountB))
            .ToList();
    }

    static string[] Fatia(IReadOnlyList<string> linhas, int prefixo, int sufixo) =>
        linhas.Skip(prefixo).Take(linhas.Count - prefixo - sufixo).ToArray();
}
