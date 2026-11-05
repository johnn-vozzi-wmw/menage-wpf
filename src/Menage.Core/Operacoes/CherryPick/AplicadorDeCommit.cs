using Menage.Core.Historico;

namespace Menage.Core.Operacoes.CherryPick;

public enum DesfechoDoCommit { Aplicado, Pulado, Abortado }

/// <summary>Um commit de cada vez: <c>-x</c> registra a origem e <c>--empty=drop</c> descarta o que já existe no destino.</summary>
public sealed class AplicadorDeCommit(IGit git, ConsultasGit consultas, IResolvedorDeConflitos resolvedor)
{
    public async Task<DesfechoDoCommit> AplicarAsync(Projeto projeto, CommitResumido commit, CancellationToken cancelar = default)
    {
        while (true)
        {
            cancelar.ThrowIfCancellationRequested();
            var args = CherryPick(projeto).Opcao("-x").Opcao("--empty=drop").Argumento(commit.Hash);
            if ((await git.ExecutarAsync(args, cancelar)).EhOk) return DesfechoDoCommit.Aplicado;
            if (await ResolverAsync(projeto, commit, cancelar) is { } desfecho) return desfecho;
        }
    }

    // Nulo: o cherry-pick não está mais em andamento (o usuário abortou por fora) e o commit deve ser refeito.
    async Task<DesfechoDoCommit?> ResolverAsync(Projeto projeto, CommitResumido commit, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        string? aviso = null;
        while (true)
        {
            cancelar.ThrowIfCancellationRequested();
            var decisao = await resolvedor.DecidirAsync(await ConflitoAsync(projeto, commit, aviso, cancelar), cancelar);
            aviso = null;

            var emAndamento = await consultas.RefVerificaAsync(repo, "CHERRY_PICK_HEAD", cancelar);
            if (decisao == DecisaoDeConflito.Pular)
            {
                if (emAndamento) await git.ExecutarAsync(CherryPick(projeto).Opcao("--skip"), cancelar);
                return DesfechoDoCommit.Pulado;
            }

            if (decisao == DecisaoDeConflito.Abortar)
            {
                if (emAndamento) await git.ExecutarAsync(CherryPick(projeto).Opcao("--abort"), cancelar);
                return DesfechoDoCommit.Abortado;
            }

            if (!emAndamento) return null;
            if (await consultas.TemConflitoAsync(repo, cancelar))
            {
                aviso = "Ainda há arquivos em conflito. Resolva e faça git add antes de continuar.";
                continue;
            }

            if ((await git.ExecutarAsync(CherryPick(projeto).Config("core.editor", "true").Opcao("--continue"), cancelar)).EhOk) return DesfechoDoCommit.Aplicado;
            if (await consultas.IndiceVazioAsync(repo, cancelar))
            {
                await git.ExecutarAsync(CherryPick(projeto).Opcao("--skip"), cancelar);
                return DesfechoDoCommit.Pulado;
            }
        }
    }

    static GitArgs CherryPick(Projeto projeto) => Git.Em(projeto.Caminho).Comando("cherry-pick");

    async Task<ConflitoDoGit> ConflitoAsync(Projeto projeto, CommitResumido commit, string? aviso, CancellationToken cancelar) => new(
        "Cherry-pick",
        projeto,
        $"Problema no cherry-pick de {commit.Curto} - {commit.Assunto}",
        await consultas.StatusCurtoAsync(projeto.Caminho, cancelar),
        aviso,
        "Continuar - conclui ou refaz este cherry-pick e segue",
        "Pular este commit",
        "Abortar este e os cherry-picks restantes");
}
