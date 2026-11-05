using System.Threading;
using Menage.Core.Inicializacao;

namespace Menage.App.Inicializacao;

/// <summary>
/// Uma instância por pasta de dados. A primeira segura o mutex e escuta o sinal; as seguintes só acionam o sinal e saem,
/// e a primeira traz a janela para a frente (<see cref="AoPedirJanela"/>).
/// </summary>
public sealed class InstanciaUnica : IDisposable
{
    readonly Mutex _mutex;
    readonly EventWaitHandle _sinal;
    readonly CancellationTokenSource _fim = new();
    Thread? _escuta;

    InstanciaUnica(Mutex mutex, EventWaitHandle sinal, bool primeira)
    {
        _mutex = mutex;
        _sinal = sinal;
        EhPrimeira = primeira;
    }

    public bool EhPrimeira { get; }

    /// <summary>Chamado (fora da thread da tela) quando outra instância pediu para mostrar a janela.</summary>
    public Action? AoPedirJanela { get; set; }

    public static InstanciaUnica Adquirir(string pastaDeDados, string? sufixoDeTeste = null)
    {
        var mutex = new Mutex(true, IdentidadeDaInstancia.NomeDoMutex(pastaDeDados) + sufixoDeTeste, out var primeira);
        var sinal = new EventWaitHandle(false, EventResetMode.AutoReset, IdentidadeDaInstancia.NomeDoSinal(pastaDeDados) + sufixoDeTeste);
        return new InstanciaUnica(mutex, sinal, primeira);
    }

    /// <summary>Segunda instância: pede à primeira que mostre a janela.</summary>
    public void PedirParaMostrar() => _sinal.Set();

    public void IniciarEscuta()
    {
        if (!EhPrimeira || _escuta is not null) return;
        _escuta = new Thread(Escutar) { IsBackground = true, Name = "menage-mostrar-janela" };
        _escuta.Start();
    }

    void Escutar()
    {
        var esperas = new[] { _sinal, _fim.Token.WaitHandle };
        while (WaitHandle.WaitAny(esperas) == 0) AoPedirJanela?.Invoke();
    }

    public void Dispose()
    {
        _fim.Cancel();
        _escuta?.Join(500);
        if (EhPrimeira) _mutex.ReleaseMutex();
        _mutex.Dispose();
        _sinal.Dispose();
        _fim.Dispose();
    }
}
