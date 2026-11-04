using Microsoft.Extensions.Logging;

namespace Menage.Core.Operacoes.Decoradores;

/// <summary>Registra início, duração e contagem de ok/aviso/erro de cada operação.</summary>
public sealed class ExecutorComLog(IExecutorDeOperacoes interno, ILogger<ExecutorComLog> log) : IExecutorDeOperacoes
{
    public async Task<Resultado<ResumoDoLote<TResultado>>> ExecutarAsync<TOpcoes, TResultado>(
        OperacaoEmLote<TOpcoes, TResultado> operacao, TOpcoes opcoes, SinaisDoLote sinais = default)
    {
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        log.LogInformation("Operação {Id} iniciada", operacao.Id);
        var resultado = await interno.ExecutarAsync(operacao, opcoes, sinais);
        if (resultado.EhOk)
        {
            var r = resultado.Valor;
            log.LogInformation("Operação {Id} terminou em {Ms} ms: {Oks} ok, {Avisos} aviso(s), {Erros} erro(s){Parou}",
                operacao.Id, relogio.ElapsedMilliseconds, r.Oks, r.Avisos, r.Erros, r.Interrompido ? ", interrompida" : "");
        }
        else
        {
            log.LogWarning("Operação {Id} não executada: {Erro}", operacao.Id, resultado.Erro);
        }

        return resultado;
    }
}
