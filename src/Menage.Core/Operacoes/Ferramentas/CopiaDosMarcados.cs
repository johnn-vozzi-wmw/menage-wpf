using System.Text.RegularExpressions;

namespace Menage.Core.Operacoes.Ferramentas;

public enum TipoDeCopia { Branches, Hashes, LinksDePr, UrlsDosRepositorios }

/// <summary>Monta o texto "projeto: valor" dos projetos marcados (branch, hash, link para abrir PR, URL do repositório) para colar em outro lugar.</summary>
public sealed partial class CopiaDosMarcados(IGit git, ConsultasGit consultas)
{
    public async Task<IReadOnlyList<string>> ColetarAsync(TipoDeCopia tipo, IEnumerable<Projeto> alvo, CancellationToken cancelar = default)
    {
        var linhas = new List<string>();
        foreach (var projeto in alvo)
        {
            var valor = await ValorAsync(tipo, projeto.Caminho, cancelar);
            linhas.Add($"{projeto.Nome}: {(valor.Length > 0 ? valor : "(indisponível)")}");
        }

        return linhas;
    }

    /// <summary>https://host/org/repo a partir de URLs ssh, scp-like ou https (sem credenciais nem .git); vazio se não reconhece.</summary>
    public static string NormalizarUrl(string remoto)
    {
        remoto = remoto.Trim();
        var scp = ScpLike().Match(remoto);
        if (scp.Success) return $"https://{scp.Groups["host"].Value}/{scp.Groups["caminho"].Value}".TrimEnd('/');
        if (!Uri.TryCreate(remoto, UriKind.Absolute, out var uri) || uri.Host.Length == 0) return "";

        var caminho = Regex.Replace(uri.AbsolutePath, @"\.git$", "", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)).Trim('/');
        return $"https://{uri.Host}/{caminho}";
    }

    /// <summary>Link para abrir um pull request da branch, conforme o servidor; a URL do repositório se o servidor não é conhecido.</summary>
    public static string LinkDePr(string urlDoRepositorio, string branch)
    {
        if (urlDoRepositorio.Length == 0 || branch.Length == 0) return "";

        bool Tem(string host) => urlDoRepositorio.Contains(host, StringComparison.OrdinalIgnoreCase);
        var codificada = Uri.EscapeDataString(branch);
        if (Tem("github")) return $"{urlDoRepositorio}/pull/new/{branch}";
        if (Tem("gitlab")) return $"{urlDoRepositorio}/-/merge_requests/new?merge_request[source_branch]={codificada}";
        if (Tem("bitbucket")) return $"{urlDoRepositorio}/pull-requests/new?source={codificada}";
        return Tem("dev.azure.com") || Tem("visualstudio.com") ? $"{urlDoRepositorio}/pullrequestcreate?sourceRef={codificada}" : urlDoRepositorio;
    }

    async Task<string> ValorAsync(TipoDeCopia tipo, string repo, CancellationToken cancelar)
    {
        switch (tipo)
        {
            case TipoDeCopia.Branches:
                var branch = await consultas.BranchAtualAsync(repo, cancelar);
                return branch.Length > 0 ? branch : "(detached HEAD)";
            case TipoDeCopia.Hashes:
                return (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("rev-parse").Argumento("HEAD"), cancelar)).PrimeiraLinha().Trim();
            default:
                var url = NormalizarUrl((await git.ExecutarBrutoAsync(Git.Em(repo).Comando("remote").Argumento("get-url").Argumento("origin"), cancelar)).PrimeiraLinha());
                return tipo == TipoDeCopia.UrlsDosRepositorios ? url : LinkDePr(url, await consultas.BranchAtualAsync(repo, cancelar));
        }
    }

    [GeneratedRegex(@"^(?:[^@/]+@)?(?<host>[^:/]+):(?<caminho>(?!//).+?)(?:\.git)?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex ScpLike();
}
