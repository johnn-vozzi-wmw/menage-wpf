namespace Menage.Core.Historico;

/// <summary>Lê o histórico inteiro em uma chamada ao git; a busca é feita em memória pelo <see cref="IndiceCommits"/>.</summary>
public sealed class LeitorDeHistorico(IGit git)
{
    const char Separador = '\x1f';

    /// <param name="escopo">Vazio é todas as branches; senão, um ref (branch, origin/branch ou tag).</param>
    public async Task<Resultado<IReadOnlyList<CommitLinha>>> LerAsync(string repo, string escopo = "", bool merges = true, CancellationToken cancelar = default)
    {
        var args = Git.Em(repo).Comando("log").Argumento(string.IsNullOrWhiteSpace(escopo) ? "--all" : escopo.Trim()).Opcao("--date-order");
        if (!merges) args = args.Opcao("--no-merges");
        var saida = await git.ExecutarAsync(args.Opcao("--format=%H%x1f%an%x1f%ae%x1f%at%x1f%D%x1f%P%x1f%s").ComTimeout(ConsultasGit.TempoLongo), cancelar);
        return saida.Mapear<IReadOnlyList<CommitLinha>>(s => [.. s.Linhas().Select(Interpretar).OfType<CommitLinha>()]);
    }

    /// <param name="formatoDeData">Valor de <c>--date</c> do git.</param>
    public async Task<CommitResumido> LerCommitAsync(string repo, string hash, string formatoDeData = "short", CancellationToken cancelar = default)
    {
        var args = Git.Em(repo).Comando("log").Opcao("-1").Opcao("--date=" + formatoDeData).Opcao("--format=%H%x1f%h%x1f%ad%x1f%an%x1f%s").Argumento(hash);
        var partes = (await git.ExecutarBrutoAsync(args, cancelar)).PrimeiraLinha().Split(Separador);
        return partes.Length < 5
            ? new(hash, hash[..Math.Min(9, hash.Length)], "", "", "")
            : new(partes[0], partes[1], partes[2], partes[3], partes[4]);
    }

    public async Task<IReadOnlyList<string>> BranchesDoCommitAsync(string repo, string hash, CancellationToken cancelar = default) =>
        (await git.ExecutarBrutoAsync(Git.Em(repo).Comando("branch").Opcao("-a").Opcao("--contains").Argumento(hash).Opcao("--format=%(refname:short)"), cancelar)).Linhas();

    /// <summary>Texto curto das branches que contêm o commit, para uma célula ou rótulo.</summary>
    public async Task<string> OndeEstaAsync(string repo, string hash, CancellationToken cancelar = default) =>
        ResumoBranches(BranchesLimpas(await BranchesDoCommitAsync(repo, hash, cancelar)));

    /// <summary>Sem o HEAD e sem repetir "x" quando existe "origin/x".</summary>
    public static IReadOnlyList<string> BranchesLimpas(IEnumerable<string> brutas)
    {
        // "origin" sozinho é o origin/HEAD abreviado pelo git
        var nomes = brutas.Select(b => b.Trim())
            .Where(n => n.Length > 0 && n is not "HEAD" and not "origin" && !n.EndsWith("/HEAD", StringComparison.Ordinal) && !n.StartsWith('('))
            .ToList();
        var todos = new HashSet<string>(nomes);
        return nomes.FindAll(n => n.IndexOf('/') is var i && (i < 0 || !todos.Contains(n[(i + 1)..])));
    }

    /// <summary>No máximo 3 nomes e "+N" para o resto.</summary>
    public static string ResumoBranches(IReadOnlyList<string> limpas) =>
        limpas.Count == 0 ? "(nenhuma branch)" : string.Join(", ", limpas.Take(3)) + (limpas.Count > 3 ? $" +{limpas.Count - 3}" : "");

    public async Task<Resultado<MapaBranches>> MontarMapaAsync(string repo, CancellationToken cancelar = default)
    {
        var refs = await git.ExecutarAsync(Git.Em(repo).Comando("for-each-ref").Opcao("--format=%(objectname) %(refname)").Argumento("refs/heads").Argumento("refs/remotes"), cancelar);
        if (!refs.EhOk) return Resultado.Falha<MapaBranches>(refs.Erro);

        var args = Git.Em(repo).Comando("rev-list").Opcao("--topo-order").Opcao("--parents").Opcao("--branches").Opcao("--remotes").ComTimeout(ConsultasGit.TempoLongo);
        var grafo = await git.ExecutarAsync(args, cancelar);
        return grafo.Mapear(g => MapaBranches.Montar(Pontas(refs.Valor), g.Linhas()));
    }

    static List<(string Nome, string Hash)> Pontas(SaidaProcesso refs)
    {
        var pontas = new List<(string, string)>();
        foreach (var linha in refs.Linhas())
        {
            var espaco = linha.IndexOf(' ');
            if (espaco <= 0) continue;
            var referencia = linha[(espaco + 1)..].Trim();
            var nome = referencia.StartsWith("refs/heads/", StringComparison.Ordinal) ? referencia[11..]
                : referencia.StartsWith("refs/remotes/", StringComparison.Ordinal) ? referencia[13..] : referencia;
            if (nome != "HEAD" && !nome.EndsWith("/HEAD", StringComparison.Ordinal)) pontas.Add((nome, linha[..espaco]));
        }

        return pontas;
    }

    static CommitLinha? Interpretar(string linha)
    {
        var p = linha.Split(Separador);
        if (p.Length < 7 || p[0].Length < 7) return null;
        _ = long.TryParse(p[3], out var segundos);
        var data = DateTimeOffset.FromUnixTimeSeconds(segundos).LocalDateTime;
        return new CommitLinha(p[0], p[1], p[2], p[6], p[4], data, merge: p[5].Contains(' '));
    }
}
