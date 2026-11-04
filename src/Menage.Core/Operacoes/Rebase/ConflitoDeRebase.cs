namespace Menage.Core.Operacoes.Rebase;

/// <param name="Onto">Ref completa da base.</param>
/// <param name="OntoNome">Como mostrar a base.</param>
public sealed record AlvoDoRebase(string Atual, string Onto, string OntoNome);

/// <summary>Conversa com o usuário enquanto o rebase está parado num conflito.</summary>
public sealed class ConflitoDeRebase(IGit git, ConsultasGit consultas, IResolvedorDeConflitos resolvedor)
{
    public async Task<ResultadoDoProjeto<ResultadoDeRebase>> ConduzirAsync(Projeto projeto, AlvoDoRebase alvo, CancellationToken cancelar = default)
    {
        string? aviso = null;
        while (true)
        {
            cancelar.ThrowIfCancellationRequested();
            var decisao = await resolvedor.DecidirAsync(await ConflitoAsync(projeto, alvo, aviso, cancelar), cancelar);
            if (decisao != DecisaoDeConflito.Continuar) return await AbortarAsync(projeto, decisao, cancelar);

            var (terminou, motivo) = await ContinuarAsync(projeto.Caminho, cancelar);
            aviso = motivo;
            if (terminou) return await ConferirAsync(projeto, alvo, cancelar);
        }
    }

    async Task<ResultadoDoProjeto<ResultadoDeRebase>> AbortarAsync(Projeto projeto, DecisaoDeConflito decisao, CancellationToken cancelar)
    {
        if (await consultas.RebaseEmAndamentoAsync(projeto.Caminho, cancelar))
            await git.ExecutarAsync(Git.Em(projeto.Caminho).Comando("rebase").Opcao("--abort"), cancelar);

        return decisao == DecisaoDeConflito.Pular
            ? ResultadoDoProjeto<ResultadoDeRebase>.Falha("rebase abortado pelo usuário", "A branch voltou ao estado anterior.")
            : ResultadoDoProjeto<ResultadoDeRebase>.Falha("rebase abortado pelo usuário - restantes não processados", "A branch voltou ao estado anterior.") with { InterromperLote = true };
    }

    // O usuário pode ter abortado por fora: quem confirma é a base estar no histórico.
    async Task<ResultadoDoProjeto<ResultadoDeRebase>> ConferirAsync(Projeto projeto, AlvoDoRebase alvo, CancellationToken cancelar) =>
        await consultas.EhAncestralAsync(projeto.Caminho, alvo.Onto, "HEAD", cancelar)
            ? ResultadoDoProjeto<ResultadoDeRebase>.Ok(new(alvo.OntoNome, true), $"{alvo.Atual} rebaseada sobre {alvo.OntoNome}, com solução manual")
            : ResultadoDoProjeto<ResultadoDeRebase>.Falha("rebase não concluído - abortado manualmente");

    // Terminou: não há mais rebase em andamento. Senão, o motivo volta como aviso na próxima pergunta.
    async Task<(bool Terminou, string? Motivo)> ContinuarAsync(string repo, CancellationToken cancelar)
    {
        while (true)
        {
            if (!await consultas.RebaseEmAndamentoAsync(repo, cancelar)) return (true, null);
            if (await consultas.TemConflitoAsync(repo, cancelar)) return (false, "Ainda há arquivos em conflito. Resolva e faça git add antes de continuar.");
            if ((await git.ExecutarAsync(Git.Em(repo).Comando("rebase").Config("core.editor", "true").Opcao("--continue"), cancelar)).EhOk) continue;

            if (!await consultas.RebaseEmAndamentoAsync(repo, cancelar)) return (true, null);
            if (await consultas.TemConflitoAsync(repo, cancelar) || !await consultas.IndiceVazioAsync(repo, cancelar)) return (false, null);

            // commit vazio depois da resolução: o conteúdo já existe na base
            if (!(await git.ExecutarAsync(Git.Em(repo).Comando("rebase").Opcao("--skip"), cancelar)).EhOk) return (false, null);
        }
    }

    async Task<ConflitoDoGit> ConflitoAsync(Projeto projeto, AlvoDoRebase alvo, string? aviso, CancellationToken cancelar) => new(
        "Rebase",
        projeto,
        $"Problema no rebase de {alvo.Atual} sobre {alvo.OntoNome}",
        await consultas.StatusCurtoAsync(projeto.Caminho, cancelar),
        aviso,
        "Continuar o rebase deste projeto e seguir para os próximos",
        "Abortar o rebase deste projeto e seguir para os próximos",
        "Abortar este rebase e não fazer os projetos restantes");
}
