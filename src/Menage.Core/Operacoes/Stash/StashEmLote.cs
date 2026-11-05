namespace Menage.Core.Operacoes.Stash;

/// <summary>Projeto com alterações, para o usuário dar nome ao stash.</summary>
public sealed record PendenciaDeStash(Projeto Projeto, string Branch, IReadOnlyList<string> Arquivos);

/// <param name="Nome">Vazio pula o projeto.</param>
public sealed record ItemDeStash(Projeto Projeto, string Nome);

/// <param name="NaoVersionados">Inclui arquivos novos.</param>
public sealed record OpcoesDeStash(IReadOnlyList<ItemDeStash> Itens, bool NaoVersionados = false);

public sealed record ResultadoDeStash(bool Guardou);

/// <summary>Stash com nome por projeto. A lista de pendências é a prévia: o usuário já confirmou ao dar os nomes.</summary>
public sealed class StashEmLote(IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, ConsultasGit consultas)
    : OperacaoEmLote<OpcoesDeStash, ResultadoDeStash>(projetos, dialogos, progresso)
{
    public override string Id => "git.stash";

    public override string Nome => "Stash";

    public override IReadOnlyList<Projeto> ProjetosAlvo(OpcoesDeStash opcoes) => [.. opcoes.Itens.Select(i => i.Projeto)];

    /// <summary>Só os projetos que têm o que guardar.</summary>
    public async Task<IReadOnlyList<PendenciaDeStash>> ListarPendenciasAsync(IEnumerable<Projeto> alvo, CancellationToken cancelar = default)
    {
        var pendencias = new List<PendenciaDeStash>();
        foreach (var projeto in alvo)
        {
            var arquivos = await consultas.PendenciasAsync(projeto.Caminho, cancelar);
            if (arquivos.Count == 0) continue;

            var branch = await consultas.BranchAtualAsync(projeto.Caminho, cancelar);
            if (branch.Length == 0) branch = $"(detached {await consultas.HeadCurtoAsync(projeto.Caminho, cancelar)})";
            pendencias.Add(new(projeto, branch, arquivos));
        }

        return pendencias;
    }

    protected override async Task<ResultadoDoProjeto<ResultadoDeStash>> ExecutarEmProjeto(Projeto projeto, OpcoesDeStash opcoes, CancellationToken cancelar)
    {
        var nome = opcoes.Itens.First(i => i.Projeto.Caminho == projeto.Caminho).Nome.Trim();
        if (nome.Length == 0) return ResultadoDoProjeto<ResultadoDeStash>.Ok(new(false), "pulado - sem nome de stash");

        var antes = await consultas.TopoDoStashAsync(projeto.Caminho, cancelar);
        var args = Git.Em(projeto.Caminho).Comando("stash").Argumento("push");
        if (opcoes.NaoVersionados) args = args.Opcao("--include-untracked");

        var push = await git.ExecutarAsync(args.Opcao("-m", nome), cancelar);
        if (!push.EhOk) return ResultadoDoProjeto<ResultadoDeStash>.Falha(push.Erro);

        return await consultas.TopoDoStashAsync(projeto.Caminho, cancelar) == antes
            ? ResultadoDoProjeto<ResultadoDeStash>.Aviso("nada guardado - só havia arquivos não versionados", new(false))
            : ResultadoDoProjeto<ResultadoDeStash>.Ok(new(true), $"stash \"{nome}\" criado");
    }

    protected override Task<ConclusaoDoLote<ResultadoDeStash>> ConcluirAsync(
        OpcoesDeStash opcoes, IReadOnlyList<ItemDoLote<ResultadoDeStash>> itens, bool interrompido, CancellationToken cancelar) =>
        Task.FromResult(new ConclusaoDoLote<ResultadoDeStash> { Observacoes = [new("Para ver: git stash list. Para recuperar: Stash pop.")] });
}
