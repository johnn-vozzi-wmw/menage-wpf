using Menage.Core.Operacoes.Push;

namespace Menage.Core.Operacoes.DefinirOrigin;

/// <param name="Publicar">Publica no origin (<c>push -u</c>) a branch que ainda não existe lá; sem isso ela é só avisada.</param>
public sealed record OpcoesDeDefinirOrigin(bool Publicar = true);

public sealed record ResultadoDeDefinirOrigin(string Branch, bool Publicada);

/// <summary>
/// Vincula a branch atual (sem upstream) ao origin: se já existe lá, passa a acompanhar <c>origin/&lt;branch&gt;</c>; se não,
/// publica com <c>push -u</c> (a proteção de branches vale). Branch que já tem origin e detached HEAD são ignorados com aviso.
/// </summary>
public sealed class DefinirOriginEmLote(IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, ConsultasGit consultas, EnvioParaOrigin envio)
    : OperacaoEmLote<OpcoesDeDefinirOrigin, ResultadoDeDefinirOrigin>(projetos, dialogos, progresso)
{
    public override string Id => "git.definir-origin";

    public override string Nome => "Definir o origin da branch";

    protected override async Task<Previa?> MontarPreviaAsync(OpcoesDeDefinirOrigin opcoes, IReadOnlyList<Projeto> alvo, CancellationToken cancelar)
    {
        var linhas = new List<string>();
        foreach (var projeto in alvo) linhas.Add($"{projeto.Nome}: {await DescreverAsync(projeto.Caminho, opcoes, cancelar)}");
        return new("Definir o origin da branch", "A branch atual de cada projeto será vinculada ao origin:", linhas);
    }

    protected override async Task<ResultadoDoProjeto<ResultadoDeDefinirOrigin>> ExecutarEmProjeto(Projeto projeto, OpcoesDeDefinirOrigin opcoes, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        var branch = await consultas.BranchAtualAsync(repo, cancelar);
        if (branch.Length == 0) return ResultadoDoProjeto<ResultadoDeDefinirOrigin>.Aviso("detached HEAD - sem branch para vincular");
        if (await TemUpstreamAsync(repo, cancelar)) return ResultadoDoProjeto<ResultadoDeDefinirOrigin>.Aviso($"{branch} já tem origin");

        await git.ExecutarBrutoAsync(Git.Em(repo).Comando("fetch").Opcao("--quiet").Argumento("origin").ComTimeout(ConsultasGit.TempoLongo), cancelar);
        if (await consultas.RefExisteAsync(repo, "refs/remotes/origin/" + branch, cancelar))
        {
            var vinculo = await git.ExecutarAsync(Git.Em(repo).Comando("branch").Opcao("--set-upstream-to=origin/" + branch).Argumento(branch), cancelar);
            return vinculo.EhOk
                ? ResultadoDoProjeto<ResultadoDeDefinirOrigin>.Ok(new(branch, false), $"{branch} passa a acompanhar origin/{branch}")
                : ResultadoDoProjeto<ResultadoDeDefinirOrigin>.Falha(vinculo.Erro);
        }

        if (!opcoes.Publicar) return ResultadoDoProjeto<ResultadoDeDefinirOrigin>.Aviso($"{branch} não existe no origin (marque \"publicar\" para criá-la lá)");
        var enviado = await envio.EnviarAsync(repo, branch, forcar: false, publicarNova: true, cancelar);
        return enviado.EhOk
            ? ResultadoDoProjeto<ResultadoDeDefinirOrigin>.Ok(new(branch, true), $"{branch} publicada no origin (upstream definido)")
            : ResultadoDoProjeto<ResultadoDeDefinirOrigin>.Falha("Não foi possível publicar a branch", enviado.Erro.Causa);
    }

    async Task<string> DescreverAsync(string repo, OpcoesDeDefinirOrigin opcoes, CancellationToken cancelar)
    {
        var branch = await consultas.BranchAtualAsync(repo, cancelar);
        if (branch.Length == 0) return "detached HEAD (ignorado)";
        if (await TemUpstreamAsync(repo, cancelar)) return $"{branch} já tem origin (nada a fazer)";
        if (await consultas.RefExisteAsync(repo, "refs/remotes/origin/" + branch, cancelar)) return $"{branch} passa a acompanhar origin/{branch}";
        return opcoes.Publicar ? $"{branch} não existe no origin: é publicada (push -u)" : $"{branch} não existe no origin (ignorada)";
    }

    async Task<bool> TemUpstreamAsync(string repo, CancellationToken cancelar) =>
        (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("rev-parse").Opcao("--abbrev-ref").Opcao("--symbolic-full-name").Argumento("@{u}"), cancelar)).Sucesso;
}
