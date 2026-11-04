using System.Text.RegularExpressions;
using DiffPlex;

namespace Menage.Core.Diff;

/// <summary>Destaque por palavra: diff entre as palavras de duas linhas; o que sobra de cada lado é o trecho alterado.</summary>
public sealed partial class PalavrasDiff(IDiffer differ)
{
    const int LimiteDePalavras = 20000;

    public (IReadOnlyList<Trecho> Esq, IReadOnlyList<Trecho> Dir) Comparar(string a, string b)
    {
        var (prefixo, sufixo) = Comum(a, b);
        var (meioA, meioB) = (a[prefixo..(a.Length - sufixo)], b[prefixo..(b.Length - sufixo)]);
        if (meioA.Length == 0 || meioB.Length == 0) return (Faixa(prefixo, meioA.Length), Faixa(prefixo, meioB.Length));
        var (ta, tb) = (Palavras().Matches(meioA), Palavras().Matches(meioB));
        if (ta.Count + tb.Count > LimiteDePalavras) return (Faixa(prefixo, meioA.Length), Faixa(prefixo, meioB.Length));

        var igualA = new bool[ta.Count];
        var igualB = new bool[tb.Count];
        Array.Fill(igualA, true);
        Array.Fill(igualB, true);
        var pedacos = new ChunkerFixo(ta.Select(m => m.Value).ToArray(), tb.Select(m => m.Value).ToArray());
        foreach (var bloco in differ.CreateDiffs(ChunkerFixo.Antigo, ChunkerFixo.Novo, false, false, pedacos).DiffBlocks)
        {
            Array.Fill(igualA, false, bloco.DeleteStartA, bloco.DeleteCountA);
            Array.Fill(igualB, false, bloco.InsertStartB, bloco.InsertCountB);
        }
        return (Juntar(ta, igualA, prefixo), Juntar(tb, igualB, prefixo));
    }

    [GeneratedRegex(@"\w+|\s+|.", RegexOptions.Singleline)]
    private static partial Regex Palavras();

    static (int Prefixo, int Sufixo) Comum(string a, string b)
    {
        var limite = Math.Min(a.Length, b.Length);
        var prefixo = 0;
        while (prefixo < limite && a[prefixo] == b[prefixo]) prefixo++;
        // Recua até o começo da palavra: "linha 20" x "linha VINTE" destaca a palavra inteira.
        while (prefixo > 0 && char.IsLetterOrDigit(a[prefixo - 1])) prefixo--;
        var sufixo = 0;
        while (sufixo < limite - prefixo && a[^(sufixo + 1)] == b[^(sufixo + 1)]) sufixo++;
        while (sufixo > 0 && char.IsLetterOrDigit(a[^sufixo])) sufixo--;
        return (prefixo, sufixo);
    }

    static List<Trecho> Faixa(int inicio, int tamanho) => tamanho > 0 ? [new(inicio, tamanho)] : [];

    /// <summary>Palavras alteradas seguidas (ou separadas só por um espaço) viram um destaque só.</summary>
    static List<Trecho> Juntar(MatchCollection palavras, bool[] igual, int deslocamento)
    {
        var saida = new List<Trecho>();
        for (var i = 0; i < palavras.Count; i++)
        {
            if (igual[i]) continue;
            var inicio = palavras[i].Index;
            while (true)
            {
                if (i + 1 < palavras.Count && !igual[i + 1]) i++;
                else if (i + 2 < palavras.Count && !igual[i + 2] && string.IsNullOrWhiteSpace(palavras[i + 1].Value)) i += 2;
                else break;
            }
            saida.Add(new(inicio + deslocamento, palavras[i].Index + palavras[i].Length - inicio));
        }
        return saida;
    }
}

/// <summary>Entrega ao DiffPlex listas já separadas (ele só sabe separar texto corrido).</summary>
sealed class ChunkerFixo(string[] antigo, string[] novo) : IChunker
{
    public const string Antigo = "a";
    public const string Novo = "b";

    public IReadOnlyList<string> Chunk(string str) => str == Antigo ? antigo : novo;
}
