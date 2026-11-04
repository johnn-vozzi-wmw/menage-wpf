namespace Menage.Core.Operacoes.Rebase;

public sealed record OpcoesDeRebase(string Base);

/// <param name="Sobre">Onde a branch foi rebaseada ("develop" ou "origin/develop").</param>
public sealed record ResultadoDeRebase(string Sobre, bool SolucaoManual = false);

/// <summary>
/// Alterações locais vão para o stash, a base é atualizada pelo origin sem sair da branch, o rebase roda e as alterações voltam.
/// Reescreve histórico: sujeito à proteção de branches.
/// </summary>
public sealed class RebaseEmLote(
    IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, ConsultasGit consultas, GuardaDePendencias guarda, ConflitoDeRebase conflito)
    : OperacaoEmLote<OpcoesDeRebase, ResultadoDeRebase>(projetos, dialogos, progresso)
{
    public override string Id => "git.rebase";

    public override string Nome => "Rebase";

    public override bool Destrutiva => true;

    protected override async Task<Erro?> ValidarAsync(OpcoesDeRebase opcoes, IReadOnlyList<Projeto> alvo, CancellationToken cancelar) =>
        await consultas.NomeDeBranchValidoAsync(opcoes.Base, cancelar)
            ? Validar(opcoes, alvo)
            : new Erro($"Nome de branch inválido: \"{opcoes.Base}\"");

    protected override Task<Previa?> MontarPreviaAsync(OpcoesDeRebase opcoes, IReadOnlyList<Projeto> alvo, CancellationToken cancelar) =>
        Task.FromResult<Previa?>(new Previa("Rebase", $"Fazer rebase sobre \"{opcoes.Base}\"? O histórico da branch atual será reescrito.", [.. alvo.Select(p => p.Nome)]));

    protected override async Task<ResultadoDoProjeto<ResultadoDeRebase>> ExecutarEmProjeto(Projeto projeto, OpcoesDeRebase opcoes, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        var atual = await consultas.BranchAtualAsync(repo, cancelar);
        if (atual.Length == 0) return ResultadoDoProjeto<ResultadoDeRebase>.Falha("detached HEAD - sem branch atual para o rebase", null, "Faça checkout de uma branch antes.");
        if (await consultas.RebaseEmAndamentoAsync(repo, cancelar)) return ResultadoDoProjeto<ResultadoDeRebase>.Falha("já havia um rebase em andamento", null, "Conclua ou aborte antes.");

        await consultas.AtualizarRefsAsync(repo, cancelar);
        var temOrigin = await consultas.RefExisteAsync(repo, "refs/remotes/origin/" + opcoes.Base, cancelar);
        if (!temOrigin && !await consultas.RefExisteAsync(repo, "refs/heads/" + opcoes.Base, cancelar))
            return ResultadoDoProjeto<ResultadoDeRebase>.Falha($"a branch {opcoes.Base} não existe");

        var guardou = await guarda.GuardarAsync(repo, $"Menage: antes do rebase de {atual} sobre {opcoes.Base}", cancelar);
        if (!guardou.EhOk) return ResultadoDoProjeto<ResultadoDeRebase>.Falha(guardou.Erro);

        var resultado = atual == opcoes.Base
            ? await SoPullAsync(repo, opcoes.Base, temOrigin, cancelar)
            : await RebasearAsync(projeto, atual, opcoes.Base, temOrigin, cancelar);
        return guardou.Valor && !await guarda.DevolverAsync(repo) ? ComStashPreso(resultado) : resultado;
    }

    protected override Task<ConclusaoDoLote<ResultadoDeRebase>> ConcluirAsync(
        OpcoesDeRebase opcoes, IReadOnlyList<ItemDoLote<ResultadoDeRebase>> itens, bool interrompido, CancellationToken cancelar) =>
        Task.FromResult(new ConclusaoDoLote<ResultadoDeRebase>
        {
            Observacoes = [new("O rebase reescreve o histórico: para publicar uma branch que já estava no origin, use git push --force-with-lease.")],
        });

    async Task<ResultadoDoProjeto<ResultadoDeRebase>> SoPullAsync(string repo, string baseBr, bool temOrigin, CancellationToken cancelar)
    {
        if (!temOrigin) return ResultadoDoProjeto<ResultadoDeRebase>.Aviso($"já estava em {baseBr}, que não existe no origin - nada a atualizar", new(baseBr));

        var pull = await git.ExecutarAsync(Git.Em(repo).Comando("pull").Opcao("--ff-only").Argumento("origin").Argumento(baseBr).ComTimeout(ConsultasGit.TempoLongo), cancelar);
        return pull.EhOk
            ? ResultadoDoProjeto<ResultadoDeRebase>.Ok(new(baseBr), $"já estava em {baseBr} - pull feito")
            : ResultadoDoProjeto<ResultadoDeRebase>.Falha(new Erro($"já estava em {baseBr} e o pull não é fast-forward", pull.Erro.ToString(), "Resolva manualmente."));
    }

    async Task<ResultadoDoProjeto<ResultadoDeRebase>> RebasearAsync(Projeto projeto, string atual, string baseBr, bool temOrigin, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        var (onto, nome, aviso) = await AtualizarBaseAsync(repo, baseBr, temOrigin, cancelar);

        var rebase = await git.ExecutarAsync(Git.Em(repo).Comando("rebase").Argumento(onto).ComTimeout(ConsultasGit.TempoLongo), cancelar);
        var resultado = await DesfechoAsync(rebase, projeto, new(atual, onto, nome), cancelar);
        if (aviso is null || resultado.Situacao != Situacao.Ok) return resultado;
        return resultado with { Situacao = Situacao.Aviso, Mensagem = $"{resultado.Mensagem}; {aviso}" };
    }

    async Task<ResultadoDoProjeto<ResultadoDeRebase>> DesfechoAsync(Resultado<SaidaProcesso> rebase, Projeto projeto, AlvoDoRebase alvo, CancellationToken cancelar)
    {
        if (rebase.EhOk) return ResultadoDoProjeto<ResultadoDeRebase>.Ok(new(alvo.OntoNome), $"{alvo.Atual} rebaseada sobre {alvo.OntoNome}");

        return await consultas.RebaseEmAndamentoAsync(projeto.Caminho, cancelar)
            ? await conflito.ConduzirAsync(projeto, alvo, cancelar)
            : ResultadoDoProjeto<ResultadoDeRebase>.Falha(new Erro("o rebase falhou sem iniciar", rebase.Erro.ToString()));
    }

    // Atualiza a base local sem sair da branch; se ela divergiu do origin, o rebase vai sobre origin/base.
    async Task<(string Onto, string Nome, string? Aviso)> AtualizarBaseAsync(string repo, string baseBr, bool temOrigin, CancellationToken cancelar)
    {
        var local = "refs/heads/" + baseBr;
        if (!temOrigin) return (local, baseBr, $"{baseBr} não existe no origin - usada a branch local, sem pull");

        var fetch = await git.ExecutarAsync(Git.Em(repo).Comando("fetch").Argumento("origin").Argumento($"{local}:{local}").ComTimeout(ConsultasGit.TempoLongo), cancelar);
        return fetch.EhOk
            ? (local, baseBr, null)
            : ("refs/remotes/origin/" + baseBr, "origin/" + baseBr, $"{baseBr} local divergiu do origin - rebase feito sobre origin/{baseBr}");
    }

    static ResultadoDoProjeto<ResultadoDeRebase> ComStashPreso(ResultadoDoProjeto<ResultadoDeRebase> resultado) =>
        resultado with
        {
            Situacao = Situacao.Erro,
            Mensagem = $"{resultado.Mensagem}; stash pop com conflito - alterações continuam no stash",
            Erro = new Erro("Stash pop com conflito", "As alterações continuam guardadas.", "Veja git stash list."),
        };
}
