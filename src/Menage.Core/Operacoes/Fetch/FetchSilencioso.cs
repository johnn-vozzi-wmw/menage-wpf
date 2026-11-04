namespace Menage.Core.Operacoes.Fetch;

/// <summary>Fetch automático: em paralelo, sem resumo nem diálogo. Quem chama decide o que fazer com as falhas.</summary>
public sealed class FetchSilencioso(IGit git)
{
    const int Paralelismo = 4;

    /// <summary>Quantos projetos falharam; pastas que não são repositório são ignoradas.</summary>
    public async Task<int> ExecutarAsync(IEnumerable<Projeto> projetos, CancellationToken cancelar = default)
    {
        var falhas = 0;
        var opcoes = new ParallelOptions { MaxDegreeOfParallelism = Paralelismo, CancellationToken = cancelar };
        await Parallel.ForEachAsync(projetos.Where(p => ConsultasGit.EhRepositorio(p.Caminho)), opcoes, async (projeto, c) =>
        {
            var args = Git.Em(projeto.Caminho).Comando("fetch").Opcao("--all").Opcao("--quiet").ComTimeout(ConsultasGit.TempoLongo);
            if (!(await git.ExecutarBrutoAsync(args, c)).Sucesso) Interlocked.Increment(ref falhas);
        });
        return falhas;
    }
}
