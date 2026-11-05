using System.Text.RegularExpressions;
using Menage.Core.Historico;

namespace Menage.Core.Operacoes.BuscarCommit;

/// <param name="Termo">Trecho da mensagem ou hash (7 caracteres ou mais).</param>
/// <param name="Fetch">Atualiza as refs antes de procurar.</param>
/// <param name="MaximoDeCommits">Por projeto, na busca por mensagem.</param>
public sealed record OpcoesDeBuscaDeCommit(string Termo, bool Fetch = false, int MaximoDeCommits = 20);

public sealed record CommitAchado(CommitResumido Commit, IReadOnlyList<string> Branches);

public sealed record ResultadoDeBuscaDeCommit(IReadOnlyList<CommitAchado> Achados);

/// <summary>Procura por hash e por mensagem (sem diferenciar maiúsculas), em todas as branches de cada projeto.</summary>
public sealed class BuscarCommitEmLote(IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, LeitorDeHistorico historico)
    : OperacaoEmLote<OpcoesDeBuscaDeCommit, ResultadoDeBuscaDeCommit>(projetos, dialogos, progresso)
{
    public override string Id => "git.buscar-commit";

    public override string Nome => "Buscar commit";

    protected override Erro? Validar(OpcoesDeBuscaDeCommit opcoes, IReadOnlyList<Projeto> alvo) =>
        string.IsNullOrWhiteSpace(opcoes.Termo) ? new Erro("Informe o que procurar") : base.Validar(opcoes, alvo);

    protected override async Task<ResultadoDoProjeto<ResultadoDeBuscaDeCommit>> ExecutarEmProjeto(Projeto projeto, OpcoesDeBuscaDeCommit opcoes, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        var termo = opcoes.Termo.Trim();
        if (opcoes.Fetch) await git.ExecutarBrutoAsync(Git.Em(repo).Comando("fetch").Opcao("--all").Opcao("--prune").Opcao("--quiet").ComTimeout(ConsultasGit.TempoLongo), cancelar);

        var hashes = new List<string>();
        if (Regex.IsMatch(termo, "^[0-9a-fA-F]{7,}$"))
        {
            var exato = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("rev-parse").Opcao("--verify").Opcao("--quiet").Argumento(termo + "^{commit}"), cancelar);
            if (exato.Sucesso && exato.PrimeiraLinha().Trim() is { Length: > 0 } hash) hashes.Add(hash);
        }

        var args = Git.Em(repo).Comando("log").Opcao("--all").Opcao("-i").Opcao("--fixed-strings")
            .Opcao($"--max-count={opcoes.MaximoDeCommits}").Opcao("--grep=" + termo).Opcao("--format=%H").ComTimeout(ConsultasGit.TempoLongo);
        var log = await git.ExecutarAsync(args, cancelar);
        if (!log.EhOk) return ResultadoDoProjeto<ResultadoDeBuscaDeCommit>.Falha(log.Erro);
        hashes.AddRange(log.Valor.Linhas().Except(hashes));

        var achados = new List<CommitAchado>();
        foreach (var hash in hashes)
            achados.Add(new(await historico.LerCommitAsync(repo, hash, "format:%d/%m/%Y %H:%M", cancelar), await historico.BranchesDoCommitAsync(repo, hash, cancelar)));
        return ResultadoDoProjeto<ResultadoDeBuscaDeCommit>.Ok(new(achados), $"{achados.Count} commit(s)");
    }
}
