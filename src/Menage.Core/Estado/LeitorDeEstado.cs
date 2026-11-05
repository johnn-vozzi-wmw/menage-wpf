namespace Menage.Core.Estado;

/// <param name="Alteracoes">Só arquivos versionados.</param>
/// <param name="NaoVersionados">Arquivos novos (<c>??</c>), contados à parte.</param>
/// <param name="Operacao">Rebase, cherry-pick ou merge pela metade; vazio se não há.</param>
/// <param name="Falha">Por que o estado não pôde ser lido; vazio se pôde.</param>
public sealed record EstadoDoProjeto(Projeto Projeto)
{
    public string Branch { get; init; } = "";

    public string Head { get; init; } = "";

    public bool Detached { get; init; }

    public int Alteracoes { get; init; }

    public int NaoVersionados { get; init; }

    public string Operacao { get; init; } = "";

    public bool TemUpstream { get; init; }

    public int Ahead { get; init; }

    public int Behind { get; init; }

    public string Falha { get; init; } = "";
}

/// <summary>Lê o que a grade mostra de cada projeto.</summary>
public sealed class LeitorDeEstado(IGit git, ConsultasGit consultas)
{
    const int Paralelismo = 6;

    public async Task<EstadoDoProjeto> LerAsync(Projeto projeto, CancellationToken cancelar = default)
    {
        if (!ConsultasGit.EhRepositorio(projeto.Caminho)) return new(projeto) { Falha = "pasta não encontrada" };

        var branch = await consultas.BranchAtualAsync(projeto.Caminho, cancelar);
        var pendencias = await consultas.PendenciasAsync(projeto.Caminho, cancelar);
        var estado = new EstadoDoProjeto(projeto)
        {
            Branch = branch,
            Detached = branch.Length == 0,
            Head = branch.Length == 0 ? await consultas.HeadCurtoAsync(projeto.Caminho, cancelar) : "",
            NaoVersionados = pendencias.Count(l => l.StartsWith("??", StringComparison.Ordinal)),
            Alteracoes = pendencias.Count(l => !l.StartsWith("??", StringComparison.Ordinal) && !l.StartsWith("!!", StringComparison.Ordinal)),
            Operacao = await consultas.OperacaoEmAndamentoAsync(projeto.Caminho, cancelar),
        };
        return estado.Detached ? estado : await ComUpstreamAsync(estado, cancelar);
    }

    /// <summary>Em paralelo, na ordem recebida; erro em um projeto vira <c>Falha</c> daquele projeto.</summary>
    public async Task<IReadOnlyList<EstadoDoProjeto>> LerTodosAsync(IReadOnlyList<Projeto> projetos, CancellationToken cancelar = default)
    {
        var estados = new EstadoDoProjeto[projetos.Count];
        var opcoes = new ParallelOptions { MaxDegreeOfParallelism = Paralelismo, CancellationToken = cancelar };
        await Parallel.ForEachAsync(Enumerable.Range(0, projetos.Count), opcoes, async (i, c) =>
        {
            try
            {
                estados[i] = await LerAsync(projetos[i], c);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                estados[i] = new(projetos[i]) { Falha = ex.Message };
            }
        });
        return estados;
    }

    async Task<EstadoDoProjeto> ComUpstreamAsync(EstadoDoProjeto estado, CancellationToken cancelar)
    {
        var args = Git.Em(estado.Projeto.Caminho).Comando("rev-list").Opcao("--left-right").Opcao("--count").Argumento("@{u}...HEAD");
        var saida = await git.ExecutarBrutoAsync(args, cancelar);
        var partes = saida.PrimeiraLinha().Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
        return saida.Sucesso && partes.Length == 2 && int.TryParse(partes[0], out var behind) && int.TryParse(partes[1], out var ahead)
            ? estado with { TemUpstream = true, Behind = behind, Ahead = ahead }
            : estado;
    }
}
