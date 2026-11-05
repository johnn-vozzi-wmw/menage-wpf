using Menage.Core.Protecao;

namespace Menage.Core.Operacoes.Decoradores;

/// <summary>Opções de uma operação que cria commit; a convenção da equipe é conferida antes de executar.</summary>
public interface IComMensagemDeCommit
{
    string MensagemDeCommit { get; }
}

/// <summary>Recusa a operação quando a mensagem de commit não segue a convenção.</summary>
public sealed class ExecutorComConvencaoDeCommit(IExecutorDeOperacoes interno, IValidadorDeCommit validador) : IExecutorDeOperacoes
{
    public Task<Resultado<ResumoDoLote<TResultado>>> ExecutarAsync<TOpcoes, TResultado>(
        OperacaoEmLote<TOpcoes, TResultado> operacao, TOpcoes opcoes, SinaisDoLote sinais = default)
    {
        if (opcoes is IComMensagemDeCommit comMensagem && validador.Validar(comMensagem.MensagemDeCommit) is { } motivo)
            return Task.FromResult(Resultado.Falha<ResumoDoLote<TResultado>>(new Erro("Mensagem de commit fora da convenção", motivo, "Ajuste a mensagem.")));

        return interno.ExecutarAsync(operacao, opcoes, sinais);
    }
}
