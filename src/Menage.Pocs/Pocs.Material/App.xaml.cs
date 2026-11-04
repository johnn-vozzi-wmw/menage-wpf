using System.Diagnostics;
using System.Windows;
using Menage.Pocs.Comum;

namespace Menage.Pocs.Material;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = new Argumentos(e.Args);
        var janela = new MainWindow();
        MainWindow = janela;
        janela.ContentRendered += async (_, _) =>
        {
            if (args.Valor("--medir") is not { } arquivo) return;
            using var p = Process.GetCurrentProcess();
            var abertura = (DateTime.Now - p.StartTime).TotalMilliseconds;
            await new RoteiroAutomatico(janela, arquivo, args.Valor("--capturas")).ExecutarAsync(abertura);
        };
        janela.Show();
    }
}
