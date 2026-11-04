namespace Menage.Core.Operacoes.Pull;

public sealed record OpcoesDePull;

/// <param name="Branch">Vazia em detached HEAD.</param>
public sealed record ResultadoDePull(string Branch);

/// <summary>Sempre merge (<c>--no-rebase</c>), do origin.</summary>
public sealed class PullEmLote(IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, ConsultasGit consultas)
    : OperacaoEmLote<OpcoesDePull, ResultadoDePull>(projetos, dialogos, progresso)
{
    public override string Id => "git.pull";

    public override string Nome => "Pull";

    /// <summary>Os mesmos argumentos valem para o sync.</summary>
    public static GitArgs Comando(string repo) =>
        Git.Em(repo).Comando("pull").Opcao("--progress").Opcao("-v").Opcao("--no-rebase").Opcao("--").Argumento("origin").ComTimeout(ConsultasGit.TempoLongo);

    protected override async Task<ResultadoDoProjeto<ResultadoDePull>> ExecutarEmProjeto(Projeto projeto, OpcoesDePull opcoes, CancellationToken cancelar)
    {
        var branch = await consultas.BranchAtualAsync(projeto.Caminho, cancelar);
        var pull = await git.ExecutarAsync(Comando(projeto.Caminho), cancelar);
        if (!pull.EhOk) return ResultadoDoProjeto<ResultadoDePull>.Falha(pull.Erro);

        return ResultadoDoProjeto<ResultadoDePull>.Ok(new(branch), "pull concluído" + (branch.Length > 0 ? " em " + branch : ""));
    }
}
