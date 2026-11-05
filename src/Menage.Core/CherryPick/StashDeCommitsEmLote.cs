using Menage.Core.Operacoes.CherryPick;

namespace Menage.Core.CherryPick;

public sealed record OpcoesDeStashDeCommits(IReadOnlyList<PlanoDeCherryPick> Planos);

public sealed record ResultadoDeStashDeCommits(int Criados);

/// <summary>
/// Cada commit marcado vira um stash próprio (base = pai do commit, árvore = a do commit), guardado com <c>git stash store</c>.
/// Não mexe na pasta de trabalho nem na branch; aplicar o stash equivale a um cherry-pick sem commit.
/// </summary>
public sealed class StashDeCommitsEmLote(IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git)
    : OperacaoEmLote<OpcoesDeStashDeCommits, ResultadoDeStashDeCommits>(projetos, dialogos, progresso)
{
    public override string Id => "git.stash-commits";

    public override string Nome => "Stash dos commits marcados";

    public override IReadOnlyCollection<PerfilDeUso> PerfisQueLiberam => ConjuntosDePerfis.Somente(PerfilDeUso.Geral, PerfilDeUso.Desenvolvedor);

    public override IReadOnlyList<Projeto> ProjetosAlvo(OpcoesDeStashDeCommits opcoes) => [.. opcoes.Planos.Select(p => p.Projeto)];

    protected override async Task<ResultadoDoProjeto<ResultadoDeStashDeCommits>> ExecutarEmProjeto(Projeto projeto, OpcoesDeStashDeCommits opcoes, CancellationToken cancelar)
    {
        var plano = opcoes.Planos.First(p => p.Projeto.Caminho == projeto.Caminho);
        int criados = 0, semPai = 0, falhas = 0;
        foreach (var commit in plano.Commits)
        {
            var resultado = await GuardarAsync(projeto.Caminho, commit, cancelar);
            if (resultado is null) semPai++;
            else if (resultado.Value) criados++;
            else falhas++;
        }

        var texto = $"{criados} stash(es) criado(s)" + (semPai > 0 ? $", {semPai} sem commit anterior (pulado)" : "");
        var dados = new ResultadoDeStashDeCommits(criados);
        if (falhas > 0) return ResultadoDoProjeto<ResultadoDeStashDeCommits>.Falha(new Erro($"{texto}, {falhas} falhou"), dados);
        return semPai > 0 ? ResultadoDoProjeto<ResultadoDeStashDeCommits>.Aviso(texto, dados) : ResultadoDoProjeto<ResultadoDeStashDeCommits>.Ok(dados, texto);
    }

    protected override Task<ConclusaoDoLote<ResultadoDeStashDeCommits>> ConcluirAsync(
        OpcoesDeStashDeCommits opcoes, IReadOnlyList<ItemDoLote<ResultadoDeStashDeCommits>> itens, bool interrompido, CancellationToken cancelar) =>
        Task.FromResult(new ConclusaoDoLote<ResultadoDeStashDeCommits> { Observacoes = [new("Para aplicar: Stash pop. A pasta de trabalho e a branch não foram alteradas.")] });

    // Nulo: commit sem pai. Falso: não foi possível criar o stash.
    async Task<bool?> GuardarAsync(string repo, Historico.CommitResumido commit, CancellationToken cancelar)
    {
        var pai = await LinhaAsync(Git.Em(repo).Comando("rev-parse").Opcao("--verify").Opcao("-q").Argumento(commit.Hash + "^1"), cancelar);
        if (pai.Length == 0) return null;
        var arvore = await LinhaAsync(Git.Em(repo).Comando("rev-parse").Argumento(commit.Hash + "^{tree}"), cancelar);
        var arvoreDoPai = await LinhaAsync(Git.Em(repo).Comando("rev-parse").Argumento(pai + "^{tree}"), cancelar);
        var rotulo = $"Menage: {commit.Curto} {commit.Assunto}";
        var indice = await LinhaAsync(Git.Em(repo).Comando("commit-tree").Argumento(arvoreDoPai).Opcao("-p", pai).Opcao("-m", "index of " + commit.Curto), cancelar);
        if (indice.Length == 0) return false;
        var stash = await LinhaAsync(Git.Em(repo).Comando("commit-tree").Argumento(arvore).Opcao("-p", pai).Opcao("-p", indice).Opcao("-m", rotulo), cancelar);
        return stash.Length > 0 && (await git.ExecutarAsync(Git.Em(repo).Comando("stash").Argumento("store").Opcao("-m", rotulo).Argumento(stash), cancelar)).EhOk;
    }

    async Task<string> LinhaAsync(GitArgs args, CancellationToken cancelar) => (await git.ExecutarBrutoAsync(args, cancelar)).PrimeiraLinha().Trim();
}
