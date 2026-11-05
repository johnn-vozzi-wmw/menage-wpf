using Menage.Core.Interacao;
using Menage.Core.Saida;

namespace Menage.Core.Operacoes;

/// <summary>
/// Diálogos do app, exceto o resumo de lote: este vai inteiro para a Saída e só avisa <see cref="ResumoPronto"/>
/// (a tela mostra um aviso no canto). Confirmações e erros continuam sendo diálogos.
/// </summary>
public sealed class DialogosComResumoNaSaida(IServicoDeDialogos interno, SaidaDeOperacoes saida, TimeProvider? relogio = null) : IServicoDeDialogos
{
    readonly TimeProvider _relogio = relogio ?? TimeProvider.System;

    public event Action<IResumoDoLote>? ResumoPronto;

    public Task<bool> ConfirmarAsync(Previa previa) => interno.ConfirmarAsync(previa);

    public Task InformarAsync(string titulo, string mensagem) => interno.InformarAsync(titulo, mensagem);

    public Task MostrarErroAsync(Erro erro) => interno.MostrarErroAsync(erro);

    public Task MostrarResumoAsync(IResumoDoLote resumo)
    {
        foreach (var linha in FormatadorDeResumo.Linhas(resumo, _relogio.GetLocalNow().DateTime))
            saida.Escrever(linha.Texto, linha.Tipo);
        ResumoPronto?.Invoke(resumo);
        return Task.CompletedTask;
    }
}
