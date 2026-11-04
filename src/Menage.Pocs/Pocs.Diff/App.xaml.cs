using System.Diagnostics;
using System.IO;
using System.Windows;
using Menage.Pocs.Comum;

namespace Menage.Pocs.Diff;

/// <summary>
/// Uso: Pocs.Diff.exe [--origem arquivo.java] [--pasta %TEMP%\menage-poc-diff] [--medir saida.json] [--capturas pasta] [--escuro]
/// </summary>
public partial class App : Application
{
    private const string OrigemPadrao = @"C:\wmw\wmwvendas\projetos\lavenderepda\src\main\java\br\com\wmw\lavenderepda\Messages_en_US.java";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = new Argumentos(e.Args);
        var pasta = args.Valor("--pasta") ?? Path.Combine(Path.GetTempPath(), "menage-poc-diff");
        var sw = Stopwatch.StartNew();
        var (antigo, novo) = new GeradorArquivosDiff().Gerar(args.Valor("--origem") ?? OrigemPadrao, pasta);
        var geracaoMs = sw.ElapsedMilliseconds;

        var janela = new MainWindow();
        MainWindow = janela;
        janela.Show();
        janela.Carregar(antigo, novo);
        janela.Tempos["gerarArquivosMs"] = geracaoMs;
        if (args.Tem("--escuro")) janela.AplicarTema(true);
        janela.ContentRendered += async (_, _) =>
        {
            if (args.Valor("--medir") is not { } saida) return;
            using var p = Process.GetCurrentProcess();
            janela.Tempos["aberturaMs"] = (DateTime.Now - p.StartTime).TotalMilliseconds;
            await new RoteiroDiff(janela, saida, args.Valor("--capturas")).ExecutarAsync();
        };
    }
}
