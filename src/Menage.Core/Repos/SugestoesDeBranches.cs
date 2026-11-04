namespace Menage.Core.Repos;

/// <summary>Nomes para sugerir nos campos de branch.</summary>
public sealed class SugestoesDeBranches(IGit git)
{
    const string Heads = "refs/heads/";
    const string Origin = "refs/remotes/origin/";
    const string Tags = "refs/tags/";

    /// <summary>Branches locais e de origin, sem repetir nem <c>HEAD</c>.</summary>
    public async Task<IReadOnlyList<string>> BranchesAsync(IEnumerable<Projeto> projetos, CancellationToken cancelar = default)
    {
        HashSet<string> nomes = NovoConjunto(), remotas = NovoConjunto(), tags = NovoConjunto();
        foreach (var projeto in projetos)
            foreach (var referencia in await RefsAsync(projeto, ["refs/heads", "refs/remotes/origin"], cancelar))
                Classificar(referencia, nomes, remotas, tags);

        return Ordenar(nomes);
    }

    /// <summary>Para base e destino: nomes, depois "origin/nome" (explicitamente o remoto), depois as tags.</summary>
    public async Task<IReadOnlyList<string>> BasesAsync(IEnumerable<Projeto> projetos, CancellationToken cancelar = default)
    {
        HashSet<string> nomes = NovoConjunto(), remotas = NovoConjunto(), tags = NovoConjunto();
        foreach (var projeto in projetos)
            foreach (var referencia in await RefsAsync(projeto, ["refs/heads", "refs/remotes/origin", "refs/tags"], cancelar))
                Classificar(referencia, nomes, remotas, tags);

        return [.. Ordenar(nomes), .. Ordenar(remotas), .. Ordenar(tags.Where(t => !nomes.Contains(t)))];
    }

    static void Classificar(string referencia, HashSet<string> nomes, HashSet<string> remotas, HashSet<string> tags)
    {
        if (referencia.StartsWith(Heads, StringComparison.Ordinal))
        {
            nomes.Add(referencia[Heads.Length..]);
        }
        else if (referencia.StartsWith(Tags, StringComparison.Ordinal))
        {
            tags.Add(referencia[Tags.Length..]);
        }
        else if (referencia.StartsWith(Origin, StringComparison.Ordinal) && referencia[Origin.Length..] != "HEAD")
        {
            nomes.Add(referencia[Origin.Length..]);
            remotas.Add("origin/" + referencia[Origin.Length..]);
        }
    }

    async Task<IReadOnlyList<string>> RefsAsync(Projeto projeto, string[] prefixos, CancellationToken cancelar)
    {
        var args = Git.Em(projeto.Caminho).Comando("for-each-ref").Opcao("--format=%(refname)");
        foreach (var prefixo in prefixos) args = args.Argumento(prefixo);
        return (await git.ExecutarBrutoAsync(args, cancelar)).Linhas();
    }

    static HashSet<string> NovoConjunto() => new(StringComparer.OrdinalIgnoreCase);

    static string[] Ordenar(IEnumerable<string> nomes) => [.. nomes.Order(StringComparer.OrdinalIgnoreCase)];
}
