namespace Menage.Core.Operacoes.CherryPick;

/// <param name="Planos">Vêm de <see cref="PreparadorDeCherryPick"/>.</param>
/// <param name="Destino">Vazio aplica na branch atual; senão troca para ela antes.</param>
public sealed record OpcoesDeCherryPick(IReadOnlyList<PlanoDeCherryPick> Planos, string Destino = "");

public sealed record ResultadoDeCherryPick(int Aplicados, int Pulados, int NaoExecutados);

/// <summary>Aplica os planos, projeto a projeto. Abortar num conflito para o lote inteiro.</summary>
public sealed class CherryPickEmLote(
    IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git, ConsultasGit consultas, AplicadorDeCommit aplicador)
    : OperacaoEmLote<OpcoesDeCherryPick, ResultadoDeCherryPick>(projetos, dialogos, progresso)
{
    public override string Id => "git.cherry-pick";

    public override string Nome => "Cherry-pick";

    public override IReadOnlyList<Projeto> ProjetosAlvo(OpcoesDeCherryPick opcoes) => [.. opcoes.Planos.Select(p => p.Projeto)];

    protected override Task<Previa?> MontarPreviaAsync(OpcoesDeCherryPick opcoes, IReadOnlyList<Projeto> alvo, CancellationToken cancelar)
    {
        var detalhes = opcoes.Planos.Select(p => p.Falha is null ? $"{p.Projeto.Nome}: {p.Commits.Count} commit(s) em {p.Destino}" : $"{p.Projeto.Nome}: {p.Falha}");
        var total = opcoes.Planos.Sum(p => p.Commits.Count);
        return Task.FromResult<Previa?>(new Previa("Cherry-pick", $"Aplicar {total} commit(s)?", [.. detalhes]));
    }

    protected override async Task<ResultadoDoProjeto<ResultadoDeCherryPick>> ExecutarEmProjeto(Projeto projeto, OpcoesDeCherryPick opcoes, CancellationToken cancelar)
    {
        var plano = opcoes.Planos.First(p => p.Projeto.Caminho == projeto.Caminho);
        if (plano.Falha is not null) return ResultadoDoProjeto<ResultadoDeCherryPick>.Falha(plano.Falha);
        if (plano.Commits.Count == 0) return ResultadoDoProjeto<ResultadoDeCherryPick>.Aviso("nenhum commit para aplicar");

        var destino = opcoes.Destino.Trim();
        if (destino.Length > 0 && !await TrocarParaDestinoAsync(projeto.Caminho, destino, cancelar))
            return ResultadoDoProjeto<ResultadoDeCherryPick>.Falha(new Erro($"falhou - checkout do destino \"{destino}\"", null, "Verifique alterações locais."));

        return await AplicarAsync(projeto, plano, cancelar);
    }

    protected override Task<ConclusaoDoLote<ResultadoDeCherryPick>> ConcluirAsync(
        OpcoesDeCherryPick opcoes, IReadOnlyList<ItemDoLote<ResultadoDeCherryPick>> itens, bool interrompido, CancellationToken cancelar) =>
        Task.FromResult(new ConclusaoDoLote<ResultadoDeCherryPick> { Observacoes = [new("Os commits foram aplicados apenas localmente. Use o push quando quiser publicar.")] });

    async Task<ResultadoDoProjeto<ResultadoDeCherryPick>> AplicarAsync(Projeto projeto, PlanoDeCherryPick plano, CancellationToken cancelar)
    {
        int aplicados = 0, pulados = 0, naoExecutados = 0;
        var abortou = false;
        foreach (var commit in plano.Commits)
        {
            if (abortou)
            {
                naoExecutados++;
                continue;
            }

            var desfecho = await aplicador.AplicarAsync(projeto, commit, cancelar);
            if (desfecho == DesfechoDoCommit.Aplicado) aplicados++;
            else if (desfecho == DesfechoDoCommit.Pulado) pulados++;
            else (abortou, naoExecutados) = (true, naoExecutados + 1);
        }

        var texto = $"{aplicados} aplicado(s), {pulados} pulado(s), {naoExecutados} não executado(s)";
        var dados = new ResultadoDeCherryPick(aplicados, pulados, naoExecutados);
        if (naoExecutados > 0) return ResultadoDoProjeto<ResultadoDeCherryPick>.Falha(new Erro(texto), dados) with { InterromperLote = abortou };
        return pulados > 0 ? ResultadoDoProjeto<ResultadoDeCherryPick>.Aviso(texto, dados) : ResultadoDoProjeto<ResultadoDeCherryPick>.Ok(dados, texto);
    }

    async Task<bool> TrocarParaDestinoAsync(string repo, string destino, CancellationToken cancelar)
    {
        var alvo = await consultas.ResolverCheckoutAsync(repo, destino, cancelar);
        return alvo?.Tipo switch
        {
            TipoDeRef.BranchLocal => (await git.ExecutarAsync(Git.Em(repo).Comando("switch").Argumento(destino), cancelar)).EhOk,
            TipoDeRef.BranchEmOrigin => (await git.ExecutarAsync(Git.Em(repo).Comando("switch").Opcao("-c").Argumento(destino).Opcao("--track").Argumento(alvo.Ref), cancelar)).EhOk,
            _ => false,
        };
    }
}
