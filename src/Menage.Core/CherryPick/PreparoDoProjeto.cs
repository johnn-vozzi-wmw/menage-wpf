namespace Menage.Core.CherryPick;

/// <summary>Antes do primeiro commit de cada projeto: pasta limpa, nada em andamento, troca para o destino, anota o HEAD e cria o backup.</summary>
public sealed class PreparoDoProjeto(IGit git, ConsultasGit consultas)
{
    /// <summary>Nulo se está pronto; senão o motivo. Se o projeto já foi preparado, só confere que continua no destino.</summary>
    public async Task<string?> PrepararAsync(FilaDeCherryPick fila, Projeto projeto, CancellationToken cancelar = default)
    {
        var repo = projeto.Caminho;
        if (!ConsultasGit.EhRepositorio(repo)) return "a pasta do projeto não foi encontrada";
        if (await consultas.RebaseEmAndamentoAsync(repo, cancelar) || await consultas.RefVerificaAsync(repo, "MERGE_HEAD", cancelar))
            return "há um rebase/merge em andamento: conclua ou aborte antes";

        var emAndamento = await consultas.RefVerificaAsync(repo, "CHERRY_PICK_HEAD", cancelar);
        if (fila.HeadInicial.ContainsKey(repo))
        {
            if (emAndamento) return "há um cherry-pick em andamento que não é desta fila: conclua ou aborte antes";
            var atual = await consultas.BranchAtualAsync(repo, cancelar);
            return atual == fila.Destino ? null : $"o projeto não está mais na branch \"{fila.Destino}\" (está em \"{(atual.Length > 0 ? atual : "detached HEAD")}\"): volte para ela";
        }

        if (emAndamento) return "há um cherry-pick em andamento: conclua ou aborte antes";
        if (!await consultas.NomeDeBranchValidoAsync(fila.Destino, cancelar)) return "nome de branch destino inválido";
        if ((await consultas.PendenciasAsync(repo, cancelar)).Count > 0) return "há alterações locais na pasta (faça commit ou stash antes)";

        await consultas.AtualizarRefsAsync(repo, cancelar);
        var alvo = await consultas.ResolverCheckoutAsync(repo, fila.Destino, cancelar);
        if (alvo is null) return $"a branch destino \"{fila.Destino}\" não existe";
        if (alvo.Tipo == TipoDeRef.Tag) return $"\"{fila.Destino}\" é uma tag: o destino precisa ser uma branch";
        if (await consultas.BranchAtualAsync(repo, cancelar) != fila.Destino && !await TrocarAsync(repo, fila.Destino, alvo, cancelar))
            return $"não foi possível trocar para \"{fila.Destino}\"";

        if (fila.AtualizarDestino && await consultas.RefExisteAsync(repo, "refs/remotes/origin/" + fila.Destino, cancelar))
            await git.ExecutarAsync(Git.Em(repo).Comando("merge").Opcao("--ff-only").Argumento("origin/" + fila.Destino), cancelar);

        var head = (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("rev-parse").Argumento("HEAD"), cancelar)).PrimeiraLinha();
        fila.HeadInicial[repo] = head;
        if (fila.Backup) await CriarBackupAsync(fila, repo, head, cancelar);
        return null;
    }

    async Task<bool> TrocarAsync(string repo, string destino, RefResolvida alvo, CancellationToken cancelar)
    {
        var args = alvo.Tipo == TipoDeRef.BranchLocal
            ? Git.Em(repo).Comando("switch").Argumento(destino)
            : Git.Em(repo).Comando("switch").Opcao("-c").Argumento(destino).Opcao("--track").Argumento(alvo.Ref);
        return (await git.ExecutarAsync(args, cancelar)).EhOk;
    }

    async Task CriarBackupAsync(FilaDeCherryPick fila, string repo, string head, CancellationToken cancelar)
    {
        var limpo = new string([.. fila.Destino.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-')]);
        var nome = $"backup/cherry-{limpo}-{DateTime.Now:yyyyMMdd-HHmmss}";
        if ((await git.ExecutarAsync(Git.Em(repo).Comando("branch").Argumento(nome).Argumento(head), cancelar)).EhOk) fila.Backups[repo] = nome;
    }
}
