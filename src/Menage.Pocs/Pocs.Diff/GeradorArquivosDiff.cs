using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Menage.Pocs.Diff;

/// <summary>
/// Gera em %TEMP% o par de arquivos do teste: lê (somente leitura) um .java real de ~7,5 mil linhas, anexa
/// "linhas muito longas" a 1 de cada 25 linhas e deriva a versão nova com linhas alteradas, inseridas e removidas.
/// </summary>
public sealed class GeradorArquivosDiff
{
    private static readonly Regex Palavra = new(@"[A-Za-z]{3,}", RegexOptions.Compiled);

    public (string Antigo, string Novo) Gerar(string origem, string pastaSaida)
    {
        Directory.CreateDirectory(pastaSaida);
        var rnd = new Random(7);
        var antigo = File.ReadAllLines(origem, Encoding.UTF8).ToList();
        for (var i = 0; i < antigo.Count; i += 25) antigo[i] += " // " + CaudaLonga(rnd, 3000 + (i / 25 % 5) * 1500);

        var novo = new List<string>(antigo.Count + 200);
        for (var i = 0; i < antigo.Count; i++)
        {
            if (i % 173 == 3) continue;                       // removida
            if (i >= 3000 && i < 3012) { if (i == 3000) novo.AddRange(Enumerable.Range(1, 5).Select(n => $"// bloco reescrito {n}")); continue; }
            novo.Add(i % 37 == 5 ? Alterar(antigo[i], i) : antigo[i]);
            if (i % 211 == 10) novo.AddRange(Enumerable.Range(1, 3).Select(n => $"// linha inserida {i}-{n}"));
        }
        var a = Path.Combine(pastaSaida, "antigo.java");
        var b = Path.Combine(pastaSaida, "novo.java");
        File.WriteAllLines(a, antigo, new UTF8Encoding(false));
        File.WriteAllLines(b, novo, new UTF8Encoding(false));
        return (a, b);
    }

    private static string Alterar(string linha, int i)
    {
        var ms = Palavra.Matches(linha);
        if (ms.Count == 0) return linha + " // alterada";
        var alvo = ms[ms.Count / 2 + (i % 2 == 0 ? 0 : -ms.Count / 2)]; // meio do texto, ou a 1ª palavra
        var depois = linha.Length > 3000 ? Palavra.Matches(linha)[^1] : alvo;  // em linhas longas, muda também o fim
        var r = linha.Remove(depois.Index, depois.Length).Insert(depois.Index, depois.Value + "_v2");
        return depois.Index == alvo.Index ? r : r.Remove(alvo.Index, alvo.Length).Insert(alvo.Index, alvo.Value + "_v2");
    }

    private static string CaudaLonga(Random rnd, int tamanho)
    {
        var sb = new StringBuilder(tamanho + 40);
        while (sb.Length < tamanho) sb.Append("campo").Append(rnd.Next(10000)).Append(rnd.Next(3) == 0 ? ", " : " valor").Append(rnd.Next(100)).Append(' ');
        return sb.ToString();
    }
}
