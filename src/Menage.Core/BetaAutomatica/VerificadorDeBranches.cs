using System.Text.RegularExpressions;

namespace Menage.Core.BetaAutomatica;

public sealed record SituacaoVerificada(SituacaoDaBranch Situacao, string Detalhe)
{
    public bool Existe => Situacao is SituacaoDaBranch.Local or SituacaoDaBranch.Remoto;
}

/// <summary>
/// Confere branches e tags dos projetos de trabalho SEM trocar nada: referências locais, depois <c>fetch origin</c> (só referências).
/// O modo "sem efeito" (diagnóstico) nunca faz fetch: usa <c>ls-remote</c>.
/// </summary>
public sealed partial class VerificadorDeBranches(IGit git, ConsultasGit consultas)
{
    static readonly TimeSpan ValidadeDoFetch = TimeSpan.FromSeconds(20);

    readonly Dictionary<string, DateTime> _fetchRecente = new(StringComparer.OrdinalIgnoreCase);
    readonly object _trava = new();

    /// <summary>Ligado só durante uma consulta do monitor: um fetch por projeto a cada 20 s.</summary>
    public bool CacheDeFetch
    {
        get => Volatile.Read(ref _cache);
        set => Volatile.Write(ref _cache, value);
    }

    bool _cache;

    public async Task<SituacaoVerificada> BranchAsync(string repo, string branch, CancellationToken cancelar = default)
    {
        if (await consultas.RefExisteAsync(repo, "refs/heads/" + branch, cancelar)) return new(SituacaoDaBranch.Local, "branch local");
        if (await consultas.RefExisteAsync(repo, "refs/remotes/origin/" + branch, cancelar)) return new(SituacaoDaBranch.Remoto, "branch em origin");

        lock (_trava)
        {
            if (!CacheDeFetch) _fetchRecente.Clear();
            if (_fetchRecente.TryGetValue(repo, out var quando) && DateTime.Now - quando < ValidadeDoFetch)
                return new(SituacaoDaBranch.Ausente, "a branch não existe local nem em origin");
        }

        var fetch = await git.ExecutarAsync(Git.Em(repo).Comando("fetch").Argumento("origin").Opcao("--prune").ComTimeout(ConsultasGit.TempoLongo), cancelar);
        if (!fetch.EhOk) return new(SituacaoDaBranch.Erro, "git fetch falhou: " + fetch.Erro);
        lock (_trava) _fetchRecente[repo] = DateTime.Now;

        return await consultas.RefExisteAsync(repo, "refs/remotes/origin/" + branch, cancelar)
            ? new(SituacaoDaBranch.Remoto, "branch em origin (após fetch)")
            : new(SituacaoDaBranch.Ausente, "a branch não existe local nem em origin");
    }

    /// <summary>Como <see cref="BranchAsync"/>, mas SÓ LEITURA: sem fetch (se não achar localmente, pergunta ao origin com ls-remote).</summary>
    public async Task<SituacaoVerificada> BranchSemEfeitoAsync(string repo, string branch, CancellationToken cancelar = default)
    {
        if (await consultas.RefExisteAsync(repo, "refs/heads/" + branch, cancelar)) return new(SituacaoDaBranch.Local, "branch local");
        if (await consultas.RefExisteAsync(repo, "refs/remotes/origin/" + branch, cancelar)) return new(SituacaoDaBranch.Remoto, "branch em origin");

        var ls = await git.ExecutarAsync(Git.Em(repo).Comando("ls-remote").Opcao("--heads").Argumento("origin").Argumento(branch), cancelar);
        if (!ls.EhOk) return new(SituacaoDaBranch.Erro, "git ls-remote falhou: " + ls.Erro);
        return ls.Valor.Texto.Trim().Length > 0
            ? new(SituacaoDaBranch.Remoto, "branch em origin (ainda não buscada; o ciclo real faria o fetch)")
            : new(SituacaoDaBranch.Ausente, "a branch não existe local nem em origin");
    }

    public async Task<SituacaoVerificada> TagAsync(string repo, string tag, CancellationToken cancelar = default)
    {
        if (await consultas.RefExisteAsync(repo, "refs/tags/" + tag, cancelar)) return new(SituacaoDaBranch.Local, "tag local");
        var ls = await git.ExecutarAsync(Git.Em(repo).Comando("ls-remote").Opcao("--tags").Argumento("origin").Argumento("refs/tags/" + tag), cancelar);
        if (!ls.EhOk) return new(SituacaoDaBranch.Erro, "git ls-remote falhou: " + ls.Erro);
        return ls.Valor.Texto.Trim().Length > 0
            ? new(SituacaoDaBranch.Remoto, "tag em origin")
            : new(SituacaoDaBranch.Ausente, $"a tag {tag} não existe local nem em origin");
    }

    /// <summary>Versão do <c>pom.xml</c> da BRANCH (lida do repositório, sem checkout); nulo se não achar.</summary>
    public async Task<string?> VersaoDaBranchAsync(string repo, string branch, CancellationToken cancelar = default)
    {
        foreach (var referencia in new[] { "refs/heads/" + branch, "refs/remotes/origin/" + branch })
        {
            if (!await consultas.RefExisteAsync(repo, referencia, cancelar)) continue;
            var show = await git.ExecutarAsync(Git.Em(repo).Comando("show").Argumento(referencia + ":pom.xml"), cancelar);
            if (show.EhOk && VersaoDoPom(show.Valor.Texto) is { } v) return v;
        }

        return null;
    }

    /// <summary>Versão do próprio projeto (a tag version filha de project); sem ela, a do parent.</summary>
    public static string? VersaoDoPom(string xml)
    {
        var limpo = ComentarioXml().Replace(xml.TrimStart('﻿'), "");
        limpo = BlocosAninhados().Replace(limpo, "");
        var propria = VersaoRaiz().Match(limpo);
        if (propria.Success) return propria.Groups[1].Value.Trim();
        var pai = Parent().Match(xml);
        return pai.Success && VersaoRaiz().Match(pai.Groups[1].Value) is { Success: true } v ? v.Groups[1].Value.Trim() : null;
    }

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex ComentarioXml();

    // remove tudo que não é filho direto de <project> que costuma ter <version> (parent, dependencies, build, profiles...)
    [GeneratedRegex("<(parent|dependencies|dependencyManagement|build|profiles|reporting|plugins|pluginManagement|properties|modules)\\b.*?</\\1>", RegexOptions.Singleline)]
    private static partial Regex BlocosAninhados();

    [GeneratedRegex("<version>\\s*([^<\\s][^<]*?)\\s*</version>")]
    private static partial Regex VersaoRaiz();

    [GeneratedRegex("<parent>(.*?)</parent>", RegexOptions.Singleline)]
    private static partial Regex Parent();
}
