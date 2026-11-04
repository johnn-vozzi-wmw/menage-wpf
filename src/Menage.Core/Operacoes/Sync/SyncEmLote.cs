using Menage.Core.Operacoes.Pull;

namespace Menage.Core.Operacoes.Sync;

public sealed record OpcoesDeSync;

/// <param name="GuardouPendencias">Havia alterações locais, que passaram pelo stash.</param>
public sealed record ResultadoDeSync(bool GuardouPendencias);

/// <summary>Stash, pull e stash pop, sem perder alterações locais.</summary>
public sealed class SyncEmLote(
    IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, ConsultasGit consultas, GuardaDePendencias guarda)
    : OperacaoEmLote<OpcoesDeSync, ResultadoDeSync>(projetos, dialogos, progresso)
{
    public override string Id => "git.sync";

    public override string Nome => "Sync";

    protected override async Task<ResultadoDoProjeto<ResultadoDeSync>> ExecutarEmProjeto(Projeto projeto, OpcoesDeSync opcoes, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        if (!ConsultasGit.EhRepositorio(repo)) return ResultadoDoProjeto<ResultadoDeSync>.Falha("pasta não encontrada");

        var branch = await consultas.BranchAtualAsync(repo, cancelar);
        if (branch.Length == 0) return ResultadoDoProjeto<ResultadoDeSync>.Aviso("detached HEAD - faça checkout de uma branch");
        if ((await consultas.OperacaoEmAndamentoAsync(repo, cancelar)).Length > 0)
            return ResultadoDoProjeto<ResultadoDeSync>.Aviso("há uma operação em andamento - conclua ou aborte antes");

        var guardou = await guarda.GuardarAsync(repo, $"Menage: sync {DateTime.Now:yyyyMMdd-HHmmss}", cancelar);
        if (!guardou.EhOk) return ResultadoDoProjeto<ResultadoDeSync>.Falha(new Erro(guardou.Erro.Causa, "O pull não foi feito."));

        var pull = await git.ExecutarAsync(PullEmLote.Comando(repo), cancelar);
        var devolveu = !guardou.Valor || await guarda.DevolverAsync(repo);
        return Desfecho(branch, guardou.Valor, pull, devolveu);
    }

    static ResultadoDoProjeto<ResultadoDeSync> Desfecho(string branch, bool guardou, Resultado<SaidaProcesso> pull, bool devolveu)
    {
        if (!pull.EhOk)
        {
            var devolucao = !guardou ? "" : devolveu ? "Suas alterações locais foram devolvidas." : "Suas alterações continuam no stash (git stash list).";
            return ResultadoDoProjeto<ResultadoDeSync>.Falha(new Erro("pull falhou", string.Join(' ', pull.Erro.ToString(), devolucao).Trim()), new(guardou));
        }

        if (!devolveu)
            return ResultadoDoProjeto<ResultadoDeSync>.Falha(
                new Erro("pull ok, mas o stash pop teve conflito", "Suas alterações continuam no stash (git stash list)."), new(guardou));

        return ResultadoDoProjeto<ResultadoDeSync>.Ok(new(guardou), $"{branch} atualizada" + (guardou ? " (suas alterações locais foram preservadas)" : ""));
    }
}
