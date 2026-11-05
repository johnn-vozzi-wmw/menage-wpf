using System.Windows;
using Microsoft.Extensions.Logging;

namespace Menage.App.Inicializacao;

/// <summary>
/// Última rede de proteção: exceção que escapou de tudo vai para o log (sem segredos) e o usuário vê uma mensagem com o caminho do log.
/// Erro na thread da tela não derruba o app (o estado costuma estar são); erro fatal de outra thread só é registrado.
/// </summary>
public sealed class TratadorGlobalDeErros(ILogger log, string pastaDeLogs, Action<string>? avisar = null)
{
    int _mostrando;

    public void Instalar(Application app)
    {
        app.DispatcherUnhandledException += (_, e) => e.Handled = Tratar(e.Exception, "tela");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Registrar(e.ExceptionObject as Exception, "fatal");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Registrar(e.Exception, "tarefa");
            e.SetObserved();
        };
    }

    /// <summary>Registra e avisa. Devolve true quando o app pode seguir.</summary>
    public bool Tratar(Exception? erro, string origem)
    {
        Registrar(erro, origem);
        if (Interlocked.Exchange(ref _mostrando, 1) == 1) return true; // erro em cascata: um aviso só
        try
        {
            var mensagem = $"Algo inesperado aconteceu e foi registrado.\n\n{erro?.Message}\n\nLog: {pastaDeLogs}";
            if (avisar is not null) avisar(mensagem);
            else MessageBox.Show(mensagem, "Menage", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Interlocked.Exchange(ref _mostrando, 0);
        }

        return true;
    }

    void Registrar(Exception? erro, string origem) => log.LogError(erro, "Exceção não tratada ({Origem})", origem);
}
