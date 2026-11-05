using System.Text;

namespace Menage.Core.Diff;

/// <summary>Regras de leitura de arquivo: UTF-8 válido (com ou sem BOM) ou, se não for, Windows-1252.</summary>
public static class Codificacoes
{
    static readonly UTF8Encoding _utf8Estrito = new(false, true);

    /// <summary>Sem registrar o provedor global de páginas de código: nada de estado compartilhado.</summary>
    public static Encoding Windows1252 { get; } = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    public static Encoding Utf8SemBom { get; } = new UTF8Encoding(false);

    public static Encoding Detectar(byte[] bytes)
    {
        try
        {
            _utf8Estrito.GetCharCount(bytes);
            return Utf8SemBom;
        }
        catch (DecoderFallbackException)
        {
            return Windows1252;
        }
    }

    public static bool TemBom(byte[] b) => b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;

    /// <summary>Texto sem o BOM: ele é tratado à parte (vira marca no diff e volta ao gravar).</summary>
    public static string Texto(byte[] bytes, Encoding codificacao)
    {
        var inicio = TemBom(bytes) ? 3 : 0;
        return codificacao.GetString(bytes, inicio, bytes.Length - inicio);
    }

    /// <summary>Como o git: um byte zero no começo do arquivo significa binário.</summary>
    public static bool Binario(byte[] bytes) => Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 8000)) >= 0;

    public static string Nome(Encoding codificacao) => codificacao.CodePage == 65001 ? "UTF-8" : "Windows-1252";
}
