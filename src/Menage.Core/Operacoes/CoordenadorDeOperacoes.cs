using Menage.Core.Interacao;
using Menage.Core.Saida;

namespace Menage.Core.Operacoes;

/// <summary>
/// Roda uma operação por vez para a tela: sinais de cancelamento ("Parar após o projeto atual"), progresso e
/// o texto da Saída. É também o <see cref="IProgresso"/> que as operações recebem.
/// </summary>
public sealed class CoordenadorDeOperacoes(IExecutorDeOperacoes executor, SaidaDeOperacoes saida) : IProgresso, IDisposable
{
    CancellationTokenSource? _pararApos;
    CancellationTokenSource? _abortar;

    public bool EmAndamento { get; private set; }

    public string Operacao { get; private set; } = "";

    public bool PararSolicitado => _pararApos?.IsCancellationRequested == true;

    /// <summary>Último passo do lote em curso (nulo fora de operação).</summary>
    public EventoDeProgresso? Passo { get; private set; }

    public event Action? Alterado;

    /// <summary>Nulo se já havia outra operação em curso. Erro de validação ou cancelamento vai para a Saída e volta no resultado.</summary>
    public async Task<Resultado<ResumoDoLote<TResultado>>?> ExecutarAsync<TOpcoes, TResultado>(
        OperacaoEmLote<TOpcoes, TResultado> operacao, TOpcoes opcoes)
    {
        if (EmAndamento) return null;
        (EmAndamento, Operacao, Passo) = (true, operacao.Nome, null);
        _pararApos = new();
        _abortar = new();
        Alterado?.Invoke();
        saida.Escrever($"> {operacao.Nome}", TipoDeLinha.Comando);
        try
        {
            var resultado = await executor.ExecutarAsync(operacao, opcoes, new SinaisDoLote(_abortar.Token, _pararApos.Token));
            if (resultado.EhOk) return resultado;
            if (resultado.Erro == Erro.Cancelado) saida.Escrever("Cancelado.", TipoDeLinha.Fraco);
            else saida.Escrever(resultado.Erro.ToString(), TipoDeLinha.Erro);
            return resultado;
        }
        finally
        {
            (EmAndamento, Passo) = (false, null);
            _pararApos.Dispose();
            _abortar.Dispose();
            (_pararApos, _abortar) = (null, null);
            Alterado?.Invoke();
        }
    }

    public void Dispose()
    {
        _pararApos?.Dispose();
        _abortar?.Dispose();
    }

    /// <summary>Deixa o projeto atual terminar e não começa o próximo.</summary>
    public void PararApos()
    {
        if (!EmAndamento || PararSolicitado) return;
        _pararApos!.Cancel();
        saida.Escrever("Parando após o projeto atual...", TipoDeLinha.Aviso);
        Alterado?.Invoke();
    }

    public void Relatar(EventoDeProgresso evento)
    {
        Passo = evento;
        if (evento.Fase == FaseDoProjeto.Iniciou) saida.Escrever($"[{evento.Indice}/{evento.Total}] {evento.Projeto}...", TipoDeLinha.Fraco);
        Alterado?.Invoke();
    }
}
