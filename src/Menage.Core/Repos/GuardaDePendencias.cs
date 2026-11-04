namespace Menage.Core.Repos;

/// <summary>Guarda as pendências em stash antes de uma operação e devolve no final (rebase e sync).</summary>
public sealed class GuardaDePendencias(IGit git, ConsultasGit consultas)
{
    /// <summary>Verdadeiro se guardou; falso se não havia nada. O topo do stash confirma que o git guardou mesmo.</summary>
    public async Task<Resultado<bool>> GuardarAsync(string repo, string mensagem, CancellationToken cancelar = default)
    {
        if ((await consultas.PendenciasAsync(repo, cancelar)).Count == 0) return Resultado.Ok(false);

        var antes = await consultas.TopoDoStashAsync(repo, cancelar);
        var args = Git.Em(repo).Comando("stash").Argumento("push").Opcao("--include-untracked").Opcao("-m", mensagem);
        var push = await git.ExecutarAsync(args, cancelar);
        if (!push.EhOk) return Resultado.Falha<bool>(push.Erro);

        return await consultas.TopoDoStashAsync(repo, cancelar) == antes
            ? Resultado.Falha<bool>("Não foi possível guardar as alterações em stash")
            : Resultado.Ok(true);
    }

    /// <summary>Em conflito o git mantém o stash na lista: nada se perde. Não obedece ao cancelamento: as pendências precisam voltar.</summary>
    public async Task<bool> DevolverAsync(string repo) =>
        (await git.ExecutarAsync(Git.Em(repo).Comando("stash").Argumento("pop"))).EhOk;
}
