using System.Text;

namespace Menage.Core.Diff;

public enum FimDeLinha { Nenhum, Lf, CrLf }

/// <summary>Arquivo já decodificado: linhas sem a quebra, com o fim de linha de cada uma guardado à parte.</summary>
public sealed record ArquivoTexto(
    IReadOnlyList<string> Linhas, IReadOnlyList<FimDeLinha> Fins, Encoding Codificacao, bool TemBom)
{
    public string NomeCodificacao => Codificacoes.Nome(Codificacao);

    /// <summary>"LF", "CRLF" ou "misto". A última linha sem quebra não conta.</summary>
    public string FimDominante
    {
        get
        {
            var (lf, crlf) = (Fins.Count(f => f == FimDeLinha.Lf), Fins.Count(f => f == FimDeLinha.CrLf));
            return crlf == 0 ? "LF" : lf == 0 ? "CRLF" : "misto";
        }
    }

    public static ArquivoTexto De(byte[] bytes)
    {
        var codificacao = Codificacoes.Detectar(bytes);
        return DeTexto(Codificacoes.Texto(bytes, codificacao), codificacao, Codificacoes.TemBom(bytes));
    }

    public static ArquivoTexto DeTexto(string texto, Encoding? codificacao = null, bool temBom = false)
    {
        var linhas = new List<string>();
        var fins = new List<FimDeLinha>();
        var inicio = 0;
        while (inicio < texto.Length)
        {
            var quebra = texto.IndexOf('\n', inicio);
            if (quebra < 0)
            {
                linhas.Add(texto[inicio..]);
                fins.Add(FimDeLinha.Nenhum);
                break;
            }
            var crlf = quebra > inicio && texto[quebra - 1] == '\r';
            linhas.Add(texto[inicio..(crlf ? quebra - 1 : quebra)]);
            fins.Add(crlf ? FimDeLinha.CrLf : FimDeLinha.Lf);
            inicio = quebra + 1;
        }
        return new(linhas, fins, codificacao ?? Codificacoes.Utf8SemBom, temBom);
    }
}
