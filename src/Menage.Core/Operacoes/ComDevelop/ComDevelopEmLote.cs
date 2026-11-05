namespace Menage.Core.Operacoes.ComDevelop;

/// <param name="Base">Branch a trazer para a atual (padrão develop).</param>
/// <param name="GuardarAlteracoes">Alterações locais vão para o stash antes e voltam no fim.</param>
public sealed record OpcoesDeComDevelop(string Base = "develop", bool GuardarAlteracoes = true);

public sealed record ResultadoDeComDevelop(string Base, bool SolucaoManual = false);

/// <summary>
/// Atualiza a base pelo origin sem sair da branch atual e faz merge dela na atual. Conflito pergunta ao usuário
/// (continuar depois de resolver, pular ou abortar tudo); o seguro é abortar o merge.
/// </summary>
public sealed class ComDevelopEmLote(
    IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, ConsultasGit consultas,
    GuardaDePendencias guarda, IResolvedorDeConflitos resolvedor)
    : OperacaoEmLote<OpcoesDeComDevelop, ResultadoDeComDevelop>(projetos, dialogos, progresso)
{
    public override string Id => "git.com-develop";

    public override string Nome => "Com develop";

    protected override async Task<Erro?> ValidarAsync(OpcoesDeComDevelop opcoes, IReadOnlyList<Projeto> alvo, CancellationToken cancelar) =>
        await consultas.NomeDeBranchValidoAsync(opcoes.Base, cancelar) ? Validar(opcoes, alvo) : new Erro($"Nome de branch inválido: \"{opcoes.Base}\"");

    protected override async Task<ResultadoDoProjeto<ResultadoDeComDevelop>> ExecutarEmProjeto(Projeto projeto, OpcoesDeComDevelop opcoes, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        var atual = await consultas.BranchAtualAsync(repo, cancelar);
        if (atual.Length == 0) return ResultadoDoProjeto<ResultadoDeComDevelop>.Falha("detached HEAD - sem branch atual", null, "Faça checkout de uma branch antes.");
        if (await consultas.OperacaoEmAndamentoAsync(repo, cancelar) is { Length: > 0 } andamento) return ResultadoDoProjeto<ResultadoDeComDevelop>.Falha($"{andamento} - conclua ou aborte antes");
        if (atual == opcoes.Base) return ResultadoDoProjeto<ResultadoDeComDevelop>.Aviso($"já está em {opcoes.Base}: use o Pull");

        await consultas.AtualizarRefsAsync(repo, cancelar);
        var origem = await EscolherOrigemAsync(repo, opcoes.Base, cancelar);
        if (origem is null) return ResultadoDoProjeto<ResultadoDeComDevelop>.Falha($"a branch {opcoes.Base} não existe");

        if (!opcoes.GuardarAlteracoes && (await consultas.PendenciasAsync(repo, cancelar)).Count > 0)
            return ResultadoDoProjeto<ResultadoDeComDevelop>.Falha("há alterações locais", null, "Guarde as alterações em stash ou marque a opção.");

        var guardou = opcoes.GuardarAlteracoes ? await guarda.GuardarAsync(repo, $"Menage: antes do merge de {opcoes.Base} em {atual}", cancelar) : Resultado.Ok(false);
        if (!guardou.EhOk) return ResultadoDoProjeto<ResultadoDeComDevelop>.Falha(guardou.Erro);

        var resultado = await MesclarAsync(projeto, atual, origem.Value, opcoes.Base, cancelar);
        return guardou.Valor && !await guarda.DevolverAsync(repo)
            ? resultado with { Situacao = Situacao.Erro, Mensagem = resultado.Mensagem + "; stash pop com conflito - alterações continuam no stash", Erro = new Erro("Stash pop com conflito", "As alterações continuam guardadas.", "Veja git stash list.") }
            : resultado;
    }

    // Atualiza a base local pelo origin sem trocar de branch; se divergiu, mescla origin/base.
    async Task<(string Ref, string Nome)?> EscolherOrigemAsync(string repo, string baseBr, CancellationToken cancelar)
    {
        var temOrigin = await consultas.RefExisteAsync(repo, "refs/remotes/origin/" + baseBr, cancelar);
        var temLocal = await consultas.RefExisteAsync(repo, "refs/heads/" + baseBr, cancelar);
        if (!temOrigin) return temLocal ? ("refs/heads/" + baseBr, baseBr) : null;

        var local = "refs/heads/" + baseBr;
        var atualizou = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("fetch").Argumento("origin").Argumento($"{local}:{local}").ComTimeout(ConsultasGit.TempoLongo), cancelar);
        return atualizou.Sucesso ? (local, baseBr) : ("refs/remotes/origin/" + baseBr, "origin/" + baseBr);
    }

    async Task<ResultadoDoProjeto<ResultadoDeComDevelop>> MesclarAsync(Projeto projeto, string atual, (string Ref, string Nome) origem, string baseBr, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        var merge = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("merge").Opcao("--no-edit").Argumento(origem.Ref).ComTimeout(ConsultasGit.TempoLongo), cancelar);
        if (merge.Sucesso) return ResultadoDoProjeto<ResultadoDeComDevelop>.Ok(new(baseBr), $"{origem.Nome} mesclada em {atual}");
        if (!await consultas.RefVerificaAsync(repo, "MERGE_HEAD", cancelar))
            return ResultadoDoProjeto<ResultadoDeComDevelop>.Falha(new Erro("o merge falhou sem iniciar", merge.TextoErro.Trim()));

        return await ConduzirConflitoAsync(projeto, atual, origem.Nome, baseBr, cancelar);
    }

    async Task<ResultadoDoProjeto<ResultadoDeComDevelop>> ConduzirConflitoAsync(Projeto projeto, string atual, string nome, string baseBr, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        string? aviso = null;
        while (true)
        {
            var conflito = new ConflitoDoGit("Merge", projeto, $"Conflito ao mesclar {nome} em {atual}", await consultas.StatusCurtoAsync(repo, cancelar), aviso,
                "Continuar o merge deste projeto e seguir para os próximos", "Abortar o merge deste projeto e seguir para os próximos", "Abortar este merge e não fazer os projetos restantes");
            var decisao = await resolvedor.DecidirAsync(conflito, cancelar);
            if (decisao != DecisaoDeConflito.Continuar) return await AbortarAsync(repo, decisao);

            if (await consultas.TemConflitoAsync(repo, cancelar)) { aviso = "Ainda há arquivos em conflito. Resolva e faça git add antes de continuar."; continue; }
            var pronto = $"{nome} mesclada em {atual}, com solução manual";
            if (!await consultas.RefVerificaAsync(repo, "MERGE_HEAD", cancelar)) return ResultadoDoProjeto<ResultadoDeComDevelop>.Ok(new(baseBr, true), pronto);

            var fim = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("commit").Opcao("--no-edit"), cancelar);
            if (fim.Sucesso) return ResultadoDoProjeto<ResultadoDeComDevelop>.Ok(new(baseBr, true), pronto);
            aviso = fim.TextoErro.Trim();
        }
    }

    async Task<ResultadoDoProjeto<ResultadoDeComDevelop>> AbortarAsync(string repo, DecisaoDeConflito decisao)
    {
        await git.ExecutarBrutoAsync(Git.Em(repo).Comando("merge").Opcao("--abort"));
        var falha = ResultadoDoProjeto<ResultadoDeComDevelop>.Falha("merge abortado pelo usuário", "A branch voltou ao estado anterior.");
        return decisao == DecisaoDeConflito.Pular
            ? falha
            : falha with { Mensagem = "merge abortado pelo usuário - restantes não processados", InterromperLote = true };
    }
}
