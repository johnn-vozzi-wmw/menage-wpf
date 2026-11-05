using System.Text;

namespace Menage.Core.Diff;

/// <summary>
/// Codificação, BOM e fim de linha de um arquivo, para gravar um resultado como o arquivo era. Com fim de linha
/// misto, as linhas da minoria continuam com o fim delas: gravar tudo no padrão da maioria marcaria essas linhas
/// como alteradas no git.
/// </summary>
public sealed class FormatoArquivo
{
    readonly HashSet<string> _minoria = new(StringComparer.Ordinal);

    FormatoArquivo(Encoding codificacao, bool bom) => (Codificacao, Bom) = (codificacao, bom);

    public Encoding Codificacao { get; }

    public bool Bom { get; }

    public bool Crlf { get; private set; }

    public bool QuebraNoFim { get; private set; } = true;

    public static FormatoArquivo De(byte[] bytes)
    {
        var arquivo = ArquivoTexto.De(bytes);
        var formato = new FormatoArquivo(arquivo.Codificacao, arquivo.TemBom)
        {
            QuebraNoFim = arquivo.Fins.Count == 0 || arquivo.Fins[^1] != FimDeLinha.Nenhum,
        };
        formato.LerFins(arquivo);
        return formato;
    }

    /// <summary>Linhas sem quebra (como estão no editor) viram os bytes do arquivo.</summary>
    public byte[] Montar(IReadOnlyList<string> linhas)
    {
        var texto = new StringBuilder();
        for (var i = 0; i < linhas.Count; i++)
        {
            var linha = linhas[i].TrimEnd('\r');
            texto.Append(linha);
            if (i == linhas.Count - 1 && !QuebraNoFim) break;
            texto.Append(_minoria.Contains(linha) != Crlf ? "\r\n" : "\n");
        }
        var corpo = Codificacao.GetBytes(texto.ToString());
        return Bom ? [0xEF, 0xBB, 0xBF, .. corpo] : corpo;
    }

    void LerFins(ArquivoTexto arquivo)
    {
        var total = arquivo.Fins.Count(f => f != FimDeLinha.Nenhum);
        var crlf = arquivo.Fins.Count(f => f == FimDeLinha.CrLf);
        Crlf = crlf > 0 && crlf * 2 >= Math.Max(1, total);
        // Só os textos que aparecem apenas com o fim da minoria: um "}" que existe com os dois fica no padrão.
        var maioria = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < arquivo.Linhas.Count; i++)
        {
            if (arquivo.Fins[i] == FimDeLinha.Nenhum) continue;
            if (arquivo.Fins[i] == FimDeLinha.CrLf != Crlf) _minoria.Add(arquivo.Linhas[i]);
            else maioria.Add(arquivo.Linhas[i]);
        }
        _minoria.ExceptWith(maioria);
    }
}
