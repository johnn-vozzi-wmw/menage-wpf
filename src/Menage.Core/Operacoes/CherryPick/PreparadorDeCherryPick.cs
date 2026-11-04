using Menage.Core.Historico;

namespace Menage.Core.Operacoes.CherryPick;

/// <param name="Destino">Texto para mostrar ("alvo (branch local)").</param>
/// <param name="Falha">Por que o projeto não pode receber os commits; nula se pode.</param>
public sealed record PlanoDeCherryPick(Projeto Projeto, string Destino, IReadOnlyList<CommitResumido> Commits, string? Falha = null);

/// <summary>
/// Acha os commits por nome (mensagem contém o termo, sem diferenciar maiúsculas) que faltam no destino. Compara por patch-id:
/// um commit já aplicado com outro hash, ou repetido em duas branches, não entra de novo.
/// </summary>
public sealed class PreparadorDeCherryPick(ConsultasGit consultas, LeitorDeHistorico historico)
{
    /// <param name="destino">Vazio é a branch atual de cada projeto.</param>
    public async Task<IReadOnlyList<PlanoDeCherryPick>> PrepararAsync(
        IEnumerable<Projeto> projetos, string termo, string destino = "", CancellationToken cancelar = default)
    {
        var planos = new List<PlanoDeCherryPick>();
        foreach (var projeto in projetos)
        {
            if (cancelar.IsCancellationRequested) break;
            await consultas.AtualizarRefsAsync(projeto.Caminho, cancelar);

            var (referencia, descricao, falha) = await ResolverDestinoAsync(projeto.Caminho, destino.Trim(), cancelar);
            planos.Add(referencia is null
                ? new(projeto, "", [], falha)
                : new(projeto, descricao, await CommitsNovosAsync(projeto.Caminho, termo, referencia, cancelar)));
        }

        return planos;
    }

    /// <summary>Plano a partir de commits escolhidos no histórico. O chamador ordena, do mais antigo para o mais novo.</summary>
    public async Task<PlanoDeCherryPick> DeCommitsAsync(Projeto projeto, IEnumerable<CommitLinha> commits, string destino = "", CancellationToken cancelar = default)
    {
        var nome = destino.Trim();
        if (nome.Length == 0)
        {
            nome = await consultas.BranchAtualAsync(projeto.Caminho, cancelar);
            if (nome.Length == 0) return new(projeto, "", [], "está em detached HEAD - informe uma branch destino");
            nome += " (branch atual)";
        }

        var lista = new List<CommitResumido>();
        foreach (var c in commits)
            lista.Add(new(c.Hash, c.Curto, c.Data.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture), c.Autor, c.Assunto, await historico.OndeEstaAsync(projeto.Caminho, c.Hash, cancelar)));
        return new(projeto, nome, lista);
    }

    async Task<(string? Referencia, string Descricao, string? Falha)> ResolverDestinoAsync(string repo, string destino, CancellationToken cancelar)
    {
        if (destino.Length == 0)
        {
            var atual = await consultas.BranchAtualAsync(repo, cancelar);
            return atual.Length == 0
                ? (null, "", "está em detached HEAD - informe uma branch destino")
                : ("HEAD", atual + " (branch atual)", null);
        }

        var alvo = await consultas.ResolverCheckoutAsync(repo, destino, cancelar);
        if (alvo is null) return (null, "", $"a branch destino \"{destino}\" não existe");
        if (alvo.Tipo == TipoDeRef.Tag) return (null, "", $"\"{destino}\" é uma tag - o destino precisa ser uma branch");

        return (alvo.Tipo == TipoDeRef.BranchLocal ? "refs/heads/" + destino : alvo.Ref, $"{destino} ({alvo.Descricao})", null);
    }

    async Task<IReadOnlyList<CommitResumido>> CommitsNovosAsync(string repo, string termo, string referencia, CancellationToken cancelar)
    {
        var noDestino = PorNome(repo, termo).Argumento(referencia);
        var aplicados = (await PatchIdsAsync(repo, noDestino, cancelar)).Select(p => p.PatchId).ToHashSet();

        var foraDoDestino = PorNome(repo, termo).Opcao("--all").Opcao("--date-order").Opcao("--reverse").Opcao("--not").Argumento(referencia);
        var vistos = new HashSet<string>();
        var commits = new List<CommitResumido>();
        foreach (var (patchId, hash) in await PatchIdsAsync(repo, foraDoDestino, cancelar))
        {
            if (aplicados.Contains(patchId) || !vistos.Add(patchId)) continue;
            var commit = await historico.LerCommitAsync(repo, hash, cancelar: cancelar);
            commits.Add(commit with { Origem = await historico.OndeEstaAsync(repo, commit.Hash, cancelar) });
        }

        return commits;
    }

    static GitArgs PorNome(string repo, string termo) =>
        Git.Em(repo).Comando("log").Opcao("--no-merges").Opcao("-i").Opcao("--fixed-strings").Opcao("--grep=" + termo).Opcao("-p").ComTimeout(ConsultasGit.TempoLongo);

    async Task<List<(string PatchId, string Hash)>> PatchIdsAsync(string repo, GitArgs log, CancellationToken cancelar)
    {
        var patchId = Git.Em(repo).Comando("patch-id").Opcao("--stable").ComTimeout(ConsultasGit.TempoLongo);
        var saida = await consultas.EncadearAsync(log, patchId, cancelar);
        var pares = new List<(string, string)>();
        foreach (var linha in saida.Linhas())
            if (linha.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: >= 2 } p) pares.Add((p[0], p[1]));
        return pares;
    }
}
