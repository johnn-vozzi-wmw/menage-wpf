using Menage.Core.Estado;

namespace Menage.Core.Operacoes.Diagnostico;

/// <summary>Resultado das operações de leitura: linhas prontas para mostrar, uma por item.</summary>
public sealed record ResultadoDeConsulta(IReadOnlyList<string> Linhas);

/// <param name="Fetch">Atualiza com o origin antes de comparar.</param>
/// <param name="MaximoDeCommits">Commits listados de cada lado, por projeto.</param>
public sealed record OpcoesDeDivergencia(bool Fetch = true, int MaximoDeCommits = 20);

/// <summary>Divergência da branch atual de cada projeto com o upstream: à frente (↑), atrás (↓) e os commits de cada lado. Só leitura.</summary>
public sealed class DivergenciaEmLote(IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, LeitorDeEstado estados)
    : OperacaoEmLote<OpcoesDeDivergencia, ResultadoDeConsulta>(projetos, dialogos, progresso)
{
    public override string Id => "mais.divergencia";

    public override string Nome => "Divergência com o origin";

    protected override async Task<ResultadoDoProjeto<ResultadoDeConsulta>> ExecutarEmProjeto(Projeto projeto, OpcoesDeDivergencia opcoes, CancellationToken cancelar)
    {
        var repo = projeto.Caminho;
        if (opcoes.Fetch) await git.ExecutarBrutoAsync(Git.Em(repo).Comando("fetch").Opcao("--quiet").Argumento("origin").ComTimeout(ConsultasGit.TempoLongo), cancelar);

        var e = await estados.LerAsync(projeto, cancelar);
        if (e.Falha.Length > 0) return ResultadoDoProjeto<ResultadoDeConsulta>.Falha(e.Falha);
        if (e.Detached) return ResultadoDoProjeto<ResultadoDeConsulta>.Aviso("detached HEAD - sem branch para comparar");
        if (!e.TemUpstream) return ResultadoDoProjeto<ResultadoDeConsulta>.Aviso($"{e.Branch} - sem upstream (branch nunca publicada)");

        var linhas = new List<string>();
        if (e.Ahead > 0) linhas.AddRange(await CommitsAsync(repo, "@{u}..HEAD", "↑ à frente", opcoes.MaximoDeCommits, cancelar));
        if (e.Behind > 0) linhas.AddRange(await CommitsAsync(repo, "HEAD..@{u}", "↓ atrás", opcoes.MaximoDeCommits, cancelar));
        var resumo = $"{e.Branch}: ↑{e.Ahead} ↓{e.Behind}";
        var dados = new ResultadoDeConsulta([resumo, .. linhas]);
        return e.Ahead == 0 && e.Behind == 0
            ? ResultadoDoProjeto<ResultadoDeConsulta>.Ok(dados, $"{e.Branch} - em dia com o origin")
            : ResultadoDoProjeto<ResultadoDeConsulta>.Aviso(e.Behind > 0 && e.Ahead > 0 ? $"{resumo} - divergiu" : resumo, dados);
    }

    async Task<IReadOnlyList<string>> CommitsAsync(string repo, string faixa, string rotulo, int maximo, CancellationToken cancelar)
    {
        var log = await git.ExecutarBrutoAsync(Git.Em(repo).Comando("log").Opcao($"--max-count={maximo}").Opcao("--format=%h %an: %s").Argumento(faixa), cancelar);
        return [.. log.Linhas().Select(l => $"   {rotulo}  {l}")];
    }
}
