using System.IO;
using System.Text.Json;
using System.Windows;

namespace Menage.Diff.Roteiro;

/// <summary>
/// Uso: Menage.Diff.Roteiro --saida pasta [--so capturas|medir|mouse|conflito|motores]
/// Só escreve em repositórios descartáveis em %TEMP% e na pasta de saída. Só LÊ arquivos de C:\wmw\wmwvendas.
/// </summary>
public static class Program
{
    static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    [STAThread]
    public static void Main(string[] args)
    {
        var lista = args.ToList();
        var saida = Valor(lista, "--saida") ?? Path.Combine(Path.GetTempPath(), "menage-roteiro");
        var so = Valor(lista, "--so");
        Directory.CreateDirectory(saida);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            var resultado = new Dictionary<string, object?>();
            try
            {
                await new Execucao(saida, so, resultado).RodarAsync();
            }
            catch (Exception e)
            {
                resultado["falha"] = e.ToString();
            }
            File.WriteAllText(Path.Combine(saida, $"resultado-{so ?? "tudo"}.json"), JsonSerializer.Serialize(resultado, _json));
            app.Shutdown();
        };
        app.Run();
    }

    static string? Valor(List<string> args, string nome)
    {
        var i = args.IndexOf(nome);
        return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
    }
}
