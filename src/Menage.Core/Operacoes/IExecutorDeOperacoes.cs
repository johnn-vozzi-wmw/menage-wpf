namespace Menage.Core.Operacoes;

/// <summary>
/// Ponto único para rodar operações em lote. Perfil, proteção de branches e log são decoradores
/// desta interface (ligados por DI), nunca <c>if</c> dentro das operações.
/// </summary>
public interface IExecutorDeOperacoes
{
    Task<Resultado<ResumoDoLote<TResultado>>> ExecutarAsync<TOpcoes, TResultado>(
        OperacaoEmLote<TOpcoes, TResultado> operacao, TOpcoes opcoes, SinaisDoLote sinais = default);
}

public sealed class ExecutorDeOperacoes : IExecutorDeOperacoes
{
    public Task<Resultado<ResumoDoLote<TResultado>>> ExecutarAsync<TOpcoes, TResultado>(
        OperacaoEmLote<TOpcoes, TResultado> operacao, TOpcoes opcoes, SinaisDoLote sinais = default) =>
        operacao.ExecutarAsync(opcoes, sinais);
}
