using System.Runtime.ExceptionServices;

namespace Menage.App.Tests;

/// <summary>Roda um trecho que cria controles WPF numa thread STA própria (sem janela na tela) e repassa a exceção, se houver.</summary>
public static class WpfDeTeste
{
    public static void Executar(Action corpo)
    {
        ExceptionDispatchInfo? erro = null;
        var thread = new Thread(() =>
        {
            try
            {
                corpo();
            }
            catch (Exception ex)
            {
                erro = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        erro?.Throw();
    }
}
