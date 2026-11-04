namespace Menage.Core.Operacoes.Decoradores;

/// <summary>Recusa operações que o perfil de uso atual não libera.</summary>
public sealed class ExecutorComPerfil(IExecutorDeOperacoes interno, IPerfilAtual perfil) : IExecutorDeOperacoes
{
    public Task<Resultado<ResumoDoLote<TResultado>>> ExecutarAsync<TOpcoes, TResultado>(
        OperacaoEmLote<TOpcoes, TResultado> operacao, TOpcoes opcoes, SinaisDoLote sinais = default)
    {
        if (operacao.PerfisQueLiberam.Contains(perfil.Atual)) return interno.ExecutarAsync(operacao, opcoes, sinais);

        var erro = new Erro($"'{operacao.Nome}' não está liberada no perfil {perfil.Atual}", null, "Troque o perfil de uso nas Configurações.");
        return Task.FromResult(Resultado.Falha<ResumoDoLote<TResultado>>(erro));
    }
}
