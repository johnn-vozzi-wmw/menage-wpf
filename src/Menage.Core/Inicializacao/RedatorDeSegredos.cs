using System.Text.RegularExpressions;

namespace Menage.Core.Inicializacao;

/// <summary>Tira segredos de qualquer texto antes de ir para o log (token, senha, cabeçalho Authorization, usuário:senha em URL).</summary>
public static partial class RedatorDeSegredos
{
    public const string Marca = "***";

    public static string Limpar(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return texto ?? "";
        var r = UrlComSenha().Replace(texto, "$1" + Marca + "@");
        r = Cabecalho().Replace(r, "$1" + Marca);
        r = Atribuicao().Replace(r, "$1" + Marca);
        return TokenConhecido().Replace(r, Marca);
    }

    // https://usuario:senha@host
    [GeneratedRegex(@"(\b[a-z][a-z0-9+.-]*://)[^\s/@:]+:[^\s/@]+@", RegexOptions.IgnoreCase)]
    private static partial Regex UrlComSenha();

    // Authorization: Bearer xxx / Basic xxx
    [GeneratedRegex(@"((?:authorization|proxy-authorization)\s*[:=]\s*(?:bearer|basic|token)?\s*)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Cabecalho();

    // token=abc, "senha": "abc", password: abc, api_key=abc (o valor vai até espaço, aspas, & ou ;)
    [GeneratedRegex(@"(\b(?:token|senha|password|passwd|secret|segredo|api[-_]?key|apikey|credencial)[a-z_]*[""']?\s*[:=]\s*[""']?)(?!\*\*\*)[^\s""'&;,}]+", RegexOptions.IgnoreCase)]
    private static partial Regex Atribuicao();

    // Formatos conhecidos mesmo soltos: GitHub (ghp_, gho_, ghs_, github_pat_), Slack (xox?-), Atlassian (ATATT)
    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|xox[abprs]-[A-Za-z0-9-]{10,}|ATATT[A-Za-z0-9_=-]{20,})")]
    private static partial Regex TokenConhecido();
}
