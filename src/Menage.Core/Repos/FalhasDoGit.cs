using System.Text;
using System.Text.RegularExpressions;

namespace Menage.Core.Repos;

public enum TipoDeFalhaGit { Desconhecida, Rede, Acesso, ChaveDoHost, Repositorio, ConfiguracaoSsh }

/// <param name="Mensagem">Explicação em português; vazia quando a falha não foi reconhecida.</param>
public sealed record FalhaGit(TipoDeFalhaGit Tipo, string Host, string Mensagem);

/// <summary>Reconhece falhas de rede, acesso e chave de host no texto do git/ssh, sem adivinhar o que não conhece.</summary>
public static class FalhasDoGit
{
    static readonly Regex _octal = new(@"(?:\\[0-3][0-7]{2})+", RegexOptions.Compiled);
    static readonly Regex _getaddrinfo = new(@"getaddrinfo:\s*([^:\s]+):", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex _sshFalhou = new("ssh: .*(failure|timed out|refused|unreachable)", RegexOptions.Compiled);

    static readonly Regex[] _hosts =
    [
        new(@"(?:connect to host|resolve hostname|resolve host:?|Could not resolve host:?)\s+([A-Za-z0-9._\-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"https?://([A-Za-z0-9._\-]+)", RegexOptions.Compiled),
        new(@"git@([A-Za-z0-9._\-]+):", RegexOptions.Compiled),
    ];

    static readonly string[] _sinaisDeChaveDoHost =
    [
        "host key verification failed", "remote host identification has changed", "cannot confirm a host key in batch mode",
        "host key did not match", "host key is not cached",
    ];

    static readonly string[] _sinaisDeAcesso =
    [
        "permission denied (publickey", "permission denied, please try again", "authentication failed", "server refused our key",
        "no supported authentication methods", "could not read username", "terminal prompts disabled", "access denied", "http 401",
        "returned error: 403",
    ];

    static readonly string[] _sinaisDeRede =
    [
        "getaddrinfo", "could not resolve host", "could not resolve hostname", "name or service not known",
        "temporary failure in name resolution", "network is unreachable", "no route to host", "connection timed out",
        "operation timed out", "connection refused", "failed to connect", "ssh: connect to host", "failure when receiving data",
        "unable to access", "connection reset", "could not connect", "unknown host", "nodename nor servname",
        "unable to open connection", "host does not exist", "network error:", "fatal error: network error",
        "não é conhecido", "não foi possível resolver", "tempo limite",
    ];

    /// <summary>"n\343o \351" vira "não é": o ssh imprime acentos como escapes octais (UTF-8 quando válido, senão Latin-1).</summary>
    public static string DecodificarOctal(string texto)
    {
        if (string.IsNullOrEmpty(texto) || !texto.Contains('\\')) return texto;
        return _octal.Replace(texto, m =>
        {
            var bytes = new byte[m.Value.Length / 4];
            for (var i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(m.Value.Substring((i * 4) + 1, 3), 8);
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (ArgumentException)
            {
                return Encoding.Latin1.GetString(bytes);
            }
        });
    }

    /// <param name="texto">Stderr e stdout do git.</param>
    /// <param name="hostPadrao">Servidor do origin, usado quando a mensagem não diz (plink).</param>
    public static FalhaGit Reconhecer(string texto, string hostPadrao = "")
    {
        var t = DecodificarOctal(texto);
        var baixo = t.ToLowerInvariant();
        var host = AcharHost(t, hostPadrao);
        var noHost = host.Length > 0 ? " " + host : "";

        if (Contem(baixo, _sinaisDeChaveDoHost))
            return new(TipoDeFalhaGit.ChaveDoHost, host,
                $"A chave do servidor{noHost} não é conhecida ou mudou. Conecte uma vez por um terminal (ssh -T git@{(host.Length > 0 ? host : "servidor")}) para confirmar a chave.");
        if (Contem(baixo, _sinaisDeAcesso))
            return new(TipoDeFalhaGit.Acesso, host,
                $"O servidor{noHost} recusou o acesso. Confira se a chave SSH está carregada (Pageant/ssh-agent) ou as credenciais HTTPS, e se você tem permissão no repositório.");
        if (RepositorioNaoExiste(baixo))
            return new(TipoDeFalhaGit.Repositorio, host,
                $"O repositório remoto não foi encontrado{(host.Length > 0 ? " em " + host : "")}. Confira o endereço do origin (git remote -v) e a permissão.");
        if (EnderecoLocalInvalido(t, host))
            return new(TipoDeFalhaGit.ConfiguracaoSsh, host,
                "O SSH recebeu argumentos de outro programa (GIT_SSH_VARIANT não combina com o ssh em uso); não é problema de rede.");
        if (Contem(baixo, _sinaisDeRede) || _sshFalhou.IsMatch(baixo))
            return new(TipoDeFalhaGit.Rede, host,
                $"Sem conexão com {(host.Length > 0 ? host : "o servidor do repositório")}: o computador não conseguiu alcançá-lo (internet, VPN ou DNS fora do ar). Nada foi alterado no projeto.");
        return new(TipoDeFalhaGit.Desconhecida, host, "");
    }

    /// <summary>Servidor de uma URL de remoto (<c>ssh://git@host/...</c>, <c>git@host:org/repo</c>, <c>https://host/...</c>).</summary>
    public static string HostDaUrl(string url)
    {
        var m = Regex.Match(url, "^[a-z+]+://(?:[^/@]+@)?([^/:]+)", RegexOptions.IgnoreCase);
        if (!m.Success) m = Regex.Match(url, @"^(?:[^@/\s]+@)?([^:/\s]+):");
        return m.Success ? m.Groups[1].Value : "";
    }

    static bool Contem(string texto, string[] sinais) => Array.Exists(sinais, texto.Contains);

    static bool RepositorioNaoExiste(string baixo) =>
        baixo.Contains("repository not found") ||
        (baixo.Contains("does not appear to be a git repository") && !Contem(baixo, _sinaisDeRede));

    static string AcharHost(string texto, string hostPadrao)
    {
        foreach (var regex in _hosts)
            if (regex.Match(texto) is { Success: true } m) return m.Groups[1].Value;
        return hostPadrao;
    }

    // OpenSSH chamado com argumentos do plink: "-batch" vira "-b atch", e "atch" aparece como endereço local.
    // Só se afirma isso quando o servidor é conhecido e o nome da mensagem é outro.
    static bool EnderecoLocalInvalido(string texto, string host)
    {
        var m = _getaddrinfo.Match(texto);
        return m.Success && host.Length > 0 && !m.Groups[1].Value.Equals(host, StringComparison.OrdinalIgnoreCase);
    }
}
