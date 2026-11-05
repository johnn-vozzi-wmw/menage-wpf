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
public sealed class LeitorDeEstado(IGit git)
{
    const int Paralelismo = 6;

    public async Task<EstadoDoProjeto> LerAsync(Projeto projeto, CancellationToken cancelar = default)
    {
        if (!ConsultasGit.EhRepositorio(projeto.Caminho)) return new(projeto) { Falha = "pasta não encontrada" };

        var saida = await git.ExecutarBrutoAsync(Git.Em(projeto.Caminho).Comando("status").Opcao("--porcelain=v2").Opcao("--branch"), cancelar);
        if (!saida.Sucesso) return new(projeto) { Falha = "não foi possível ler o status do git" };

        var s = StatusPorcelainV2.Interpretar(saida.Texto);
        return new EstadoDoProjeto(projeto)
        {
            Branch = s.Branch,
            Detached = s.Detached,
            Head = s.Head,
            NaoVersionados = s.NaoVersionados,
            Alteracoes = s.Alteracoes,
            Operacao = OperacaoEmAndamento.Detectar(projeto.Caminho),
            TemUpstream = s.TemUpstream,
            Ahead = s.Ahead,
            Behind = s.Behind,
        };
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
}
