using Menage.Core.Operacoes.Decoradores;

namespace Menage.Core.Operacoes.Renomear;

/// <param name="Atual">Vazio = a branch atual de cada projeto.</param>
/// <param name="PublicarNoOrigin">Publica o nome novo, apaga o antigo do origin e ajusta o upstream.</param>
public sealed record OpcoesDeRenomear(string Novo, string Atual = "", bool PublicarNoOrigin = false);

public sealed record ResultadoDeRenomear(string De, string Para, bool NoOrigin);

/// <summary>
/// Renomeia a branch local e, se pedido, no origin. Erro se o nome novo já existe (local ou origin); branch protegida não é
/// renomeada no origin; se o origin recusar o nome novo, o rename local é desfeito.
/// </summary>
public sealed class RenomearBranchEmLote(
    IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, ConsultasGit consultas, IRegrasDeProtecao regras)
    : OperacaoEmLote<OpcoesDeRenomear, ResultadoDeRenomear>(projetos, dialogos, progresso)
{
    public override string Id => "git.renomear-branch";

    public override string Nome => "Renomear branch";

    protected override async Task<Erro?> ValidarAsync(OpcoesDeRenomear opcoes, IReadOnlyList<Projeto> alvo, CancellationToken cancelar) =>
        await consultas.NomeDeBranchValidoAsync(opcoes.Novo.Trim(), cancelar)
            ? Validar(opcoes, alvo)
            : new Erro($"Nome de branch inválido: \"{opcoes.Novo}\"");

    protected override async Task<ResultadoDoProjeto<ResultadoDeRenomear>> ExecutarEmProjeto(Projeto projeto, OpcoesDeRenomear opcoes, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        var novo = opcoes.Novo.Trim();
        var antigo = opcoes.Atual.Trim().Length > 0 ? opcoes.Atual.Trim() : await consultas.BranchAtualAsync(repo, cancelar);
        if (antigo.Length == 0) return ResultadoDoProjeto<ResultadoDeRenomear>.Falha("detached HEAD - sem branch atual para renomear");
        if (antigo == novo) return ResultadoDoProjeto<ResultadoDeRenomear>.Aviso("o nome novo é igual ao atual");

        if (opcoes.PublicarNoOrigin) await consultas.AtualizarRefsAsync(repo, cancelar);
        if (!await consultas.RefExisteAsync(repo, "refs/heads/" + antigo, cancelar)) return ResultadoDoProjeto<ResultadoDeRenomear>.Falha($"a branch local \"{antigo}\" não existe");
        if (await consultas.BranchExisteAsync(repo, novo, cancelar)) return ResultadoDoProjeto<ResultadoDeRenomear>.Falha($"a branch \"{novo}\" já existe (local ou origin)");

        var rename = await git.ExecutarAsync(Git.Em(repo).Comando("branch").Opcao("-m").Argumento(antigo).Argumento(novo), cancelar);
        if (!rename.EhOk) return ResultadoDoProjeto<ResultadoDeRenomear>.Falha(new Erro("Não foi possível renomear", rename.Erro.ToString()));
        if (!opcoes.PublicarNoOrigin) return ResultadoDoProjeto<ResultadoDeRenomear>.Ok(new(antigo, novo, false), $"{antigo} renomeada para {novo} (só local)");

        return regras.ProtegidaParaPush(antigo) || regras.ProtegidaParaExclusao(antigo)
            ? ResultadoDoProjeto<ResultadoDeRenomear>.Aviso($"{antigo} renomeada para {novo} só localmente - branch protegida não é renomeada no origin", new(antigo, novo, false))
            : await PublicarAsync(repo, antigo, novo, cancelar);
    }

    async Task<ResultadoDoProjeto<ResultadoDeRenomear>> PublicarAsync(string repo, string antigo, string novo, CancellationToken cancelar)
    {
        var tinhaNoOrigin = await consultas.RefExisteAsync(repo, "refs/remotes/origin/" + antigo, cancelar);
        var push = await git.ExecutarAsync(Git.Em(repo).Comando("push").Opcao("-u").Argumento("origin").Argumento(novo).ComTimeout(ConsultasGit.TempoLongo), cancelar);
        if (!push.EhOk)
        {
            await git.ExecutarBrutoAsync(Git.Em(repo).Comando("branch").Opcao("-m").Argumento(novo).Argumento(antigo), cancelar);
            return ResultadoDoProjeto<ResultadoDeRenomear>.Falha(new Erro("O origin recusou o nome novo; o rename local foi desfeito", push.Erro.ToString()));
        }

        if (!tinhaNoOrigin) return ResultadoDoProjeto<ResultadoDeRenomear>.Ok(new(antigo, novo, true), $"{antigo} renomeada para {novo} e publicada");

        var apagar = await git.ExecutarAsync(Git.Em(repo).Comando("push").Argumento("origin").Opcao("--delete").Argumento(antigo).ComTimeout(ConsultasGit.TempoLongo), cancelar);
        return apagar.EhOk
            ? ResultadoDoProjeto<ResultadoDeRenomear>.Ok(new(antigo, novo, true), $"{antigo} renomeada para {novo} (local e origin)")
            : ResultadoDoProjeto<ResultadoDeRenomear>.Aviso($"{novo} publicada, mas o origin não deixou apagar \"{antigo}\": {apagar.Erro.Causa}", new(antigo, novo, true));
    }
}
