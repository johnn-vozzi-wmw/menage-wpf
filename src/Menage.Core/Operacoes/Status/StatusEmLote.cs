namespace Menage.Core.Operacoes.Status;

public sealed record OpcoesDeStatus;

public sealed record ResultadoDeStatus(IReadOnlyList<string> Arquivos);

/// <summary>Projeto limpo é ok; com arquivos alterados, aviso.</summary>
public sealed class StatusEmLote(IProjetos projetos, IServicoDeDialogos dialogos, IProgresso progresso, IGit git)
    : OperacaoEmLote<OpcoesDeStatus, ResultadoDeStatus>(projetos, dialogos, progresso)
{
    public override string Id => "git.status";

    public override string Nome => "Status";

    protected override async Task<ResultadoDoProjeto<ResultadoDeStatus>> ExecutarEmProjeto(Projeto projeto, OpcoesDeStatus opcoes, CancellationToken cancelar)
    {
        var status = await git.ExecutarAsync(Git.Em(projeto.Caminho).Comando("status").Opcao("-s"), cancelar);
        if (!status.EhOk) return ResultadoDoProjeto<ResultadoDeStatus>.Falha(status.Erro);

        var arquivos = status.Valor.Linhas();
        return arquivos.Count == 0
            ? ResultadoDoProjeto<ResultadoDeStatus>.Ok(new(arquivos), "sem alterações")
            : ResultadoDoProjeto<ResultadoDeStatus>.Aviso($"{arquivos.Count} arquivo(s) alterado(s)", new(arquivos));
    }
}
