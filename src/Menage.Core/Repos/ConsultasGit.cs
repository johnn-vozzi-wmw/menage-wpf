namespace Menage.Core.Repos;

/// <summary>Perguntas e passos pequenos que todas as operações fazem ao git.</summary>
public sealed class ConsultasGit(IGit git)
{
    /// <summary>Tempo para comandos que dependem da rede ou percorrem o histórico inteiro.</summary>
    public static readonly TimeSpan TempoLongo = TimeSpan.FromMinutes(30);

    public static bool EhRepositorio(string pasta)
    {
        var marcador = Path.Combine(pasta, ".git");
        return Directory.Exists(marcador) || File.Exists(marcador);
    }

    /// <summary>Nome da branch atual; vazio em detached HEAD.</summary>
    public async Task<string> BranchAtualAsync(string repo, CancellationToken cancelar = default) =>
        (await Bruto(Git.Em(repo).Comando("branch").Opcao("--show-current"), cancelar)).PrimeiraLinha().Trim();

    public async Task<string> HeadCurtoAsync(string repo, CancellationToken cancelar = default) =>
        (await Bruto(Git.Em(repo).Comando("rev-parse").Opcao("--short").Argumento("HEAD"), cancelar)).PrimeiraLinha();

    public async Task<bool> RefExisteAsync(string repo, string refCompleta, CancellationToken cancelar = default) =>
        (await Bruto(Git.Em(repo).Comando("show-ref").Opcao("--verify").Opcao("--quiet").Argumento(refCompleta), cancelar)).Sucesso;

    public async Task<bool> BranchExisteAsync(string repo, string nome, CancellationToken cancelar = default) =>
        await RefExisteAsync(repo, "refs/heads/" + nome, cancelar) || await RefExisteAsync(repo, "refs/remotes/origin/" + nome, cancelar);

    /// <summary>Para refs especiais como <c>MERGE_HEAD</c> e <c>CHERRY_PICK_HEAD</c>.</summary>
    public async Task<bool> RefVerificaAsync(string repo, string nome, CancellationToken cancelar = default) =>
        (await Bruto(Git.Em(repo).Comando("rev-parse").Opcao("-q").Opcao("--verify").Argumento(nome), cancelar)).Sucesso;

    /// <summary>Hash do topo da pilha de stash; vazio se não há stash.</summary>
    public async Task<string> TopoDoStashAsync(string repo, CancellationToken cancelar = default) =>
        (await Bruto(Git.Em(repo).Comando("rev-parse").Opcao("-q").Opcao("--verify").Argumento("refs/stash"), cancelar)).PrimeiraLinha();

    public async Task<string> StatusCurtoAsync(string repo, CancellationToken cancelar = default) =>
        (await Bruto(Git.Em(repo).Comando("status").Opcao("--short"), cancelar)).Texto.TrimEnd();

    public async Task<IReadOnlyList<string>> PendenciasAsync(string repo, CancellationToken cancelar = default) =>
        (await Bruto(Git.Em(repo).Comando("status").Opcao("--porcelain"), cancelar)).Linhas();

    public async Task<bool> TemConflitoAsync(string repo, CancellationToken cancelar = default) =>
        (await Bruto(Git.Em(repo).Comando("diff").Opcao("--name-only").Opcao("--diff-filter=U"), cancelar)).Linhas().Count > 0;

    /// <summary>Verdadeiro se não há nada no índice (depois de resolver um conflito, o commit ficaria vazio).</summary>
    public async Task<bool> IndiceVazioAsync(string repo, CancellationToken cancelar = default) =>
        (await Bruto(Git.Em(repo).Comando("diff").Opcao("--cached").Opcao("--quiet"), cancelar)).Sucesso;

    public async Task<bool> EhAncestralAsync(string repo, string ancestral, string descendente, CancellationToken cancelar = default) =>
        (await Bruto(Git.Em(repo).Comando("merge-base").Opcao("--is-ancestor").Argumento(ancestral).Argumento(descendente), cancelar)).Sucesso;

    public async Task<bool> RebaseEmAndamentoAsync(string repo, CancellationToken cancelar = default)
    {
        foreach (var pasta in new[] { "rebase-merge", "rebase-apply" })
        {
            var caminho = (await Bruto(Git.Em(repo).Comando("rev-parse").Opcao("--git-path").Argumento(pasta), cancelar)).PrimeiraLinha();
            if (caminho.Length > 0 && Directory.Exists(Path.GetFullPath(caminho, repo))) return true;
        }

        return false;
    }

    /// <summary>Descrição da operação pela metade (rebase, cherry-pick, merge) ou vazio.</summary>
    public async Task<string> OperacaoEmAndamentoAsync(string repo, CancellationToken cancelar = default)
    {
        if (await RebaseEmAndamentoAsync(repo, cancelar)) return "rebase em andamento";
        if (await RefVerificaAsync(repo, "CHERRY_PICK_HEAD", cancelar)) return "cherry-pick em andamento";
        return await RefVerificaAsync(repo, "MERGE_HEAD", cancelar) ? "merge em andamento" : "";
    }

    /// <summary>Branches e tags do remoto. Falha é ignorada: sem rede, a operação segue com o que já tem.</summary>
    public async Task AtualizarRefsAsync(string repo, CancellationToken cancelar = default)
    {
        await Bruto(Git.Em(repo).Comando("fetch").Opcao("--all").Opcao("--prune").Opcao("--quiet").ComTimeout(TempoLongo), cancelar);
        await Bruto(Git.Em(repo).Comando("fetch").Opcao("--all").Opcao("--tags").Opcao("--quiet").ComTimeout(TempoLongo), cancelar);
    }

    public async Task<bool> NomeDeBranchValidoAsync(string nome, CancellationToken cancelar = default) =>
        !string.IsNullOrWhiteSpace(nome) &&
        (await Bruto(Git.Em(Path.GetTempPath()).Comando("check-ref-format").Opcao("--branch").Argumento(nome), cancelar)).Sucesso;

    /// <summary>Checkout: branch local, depois origin, depois tag.</summary>
    public async Task<RefResolvida?> ResolverCheckoutAsync(string repo, string nome, CancellationToken cancelar = default)
    {
        if (await RefExisteAsync(repo, "refs/heads/" + nome, cancelar)) return new(nome, TipoDeRef.BranchLocal);
        if (await RefExisteAsync(repo, "refs/remotes/origin/" + nome, cancelar)) return new("origin/" + nome, TipoDeRef.BranchEmOrigin);
        return await ResolverTagAsync(repo, nome, cancelar);
    }

    /// <summary>Base de uma branch nova: origin, depois local, depois tag; "origin/x" é exatamente o remoto.</summary>
    public async Task<RefResolvida?> ResolverBaseAsync(string repo, string nome, CancellationToken cancelar = default)
    {
        if (nome.StartsWith("origin/", StringComparison.Ordinal) && nome.Length > 7 &&
            !await RefExisteAsync(repo, "refs/heads/" + nome, cancelar) && await RefExisteAsync(repo, "refs/remotes/" + nome, cancelar))
            return new(nome, TipoDeRef.BranchEmOrigin);

        if (await RefExisteAsync(repo, "refs/remotes/origin/" + nome, cancelar)) return new("origin/" + nome, TipoDeRef.BranchEmOrigin);
        if (await RefExisteAsync(repo, "refs/heads/" + nome, cancelar)) return new("refs/heads/" + nome, TipoDeRef.BranchLocal);
        return await ResolverTagAsync(repo, nome, cancelar);
    }

    /// <summary>
    /// A saída de <paramref name="origem"/> vai, em bytes, para a entrada de <paramref name="destino"/>. A linha em branco na
    /// frente é obrigatória: com o console em UTF-8 a assinatura (BOM) entrava no início e o <c>git patch-id</c> devolvia o
    /// commit zerado, o que fazia o cherry-pick ignorar ou duplicar commits.
    /// </summary>
    public async Task<SaidaProcesso> EncadearAsync(GitArgs origem, GitArgs destino, CancellationToken cancelar = default)
    {
        var saida = await Bruto(origem, cancelar);
        return await Bruto(destino.ComEntrada([(byte)'\n', .. saida.Saida]), cancelar);
    }

    async Task<RefResolvida?> ResolverTagAsync(string repo, string nome, CancellationToken cancelar) =>
        await RefExisteAsync(repo, "refs/tags/" + nome, cancelar) ? new("refs/tags/" + nome, TipoDeRef.Tag) : null;

    Task<SaidaProcesso> Bruto(GitArgs args, CancellationToken cancelar) => git.ExecutarBrutoAsync(args, cancelar);
}
