using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Menage.Pocs.Comum;

/// <summary>O que cada tela de teste (WPF UI / Material) expõe para o roteiro automático de medição.</summary>
public interface IJanelaPoc
{
    Window Janela { get; }
    string Biblioteca { get; }
    AnimadorBarraLateral Animador { get; }
    DataGrid Grade { get; }
    void AplicarTema(bool escuro, Paleta paleta);
    void AbrirDialogo();
    void FecharDialogo();
}

/// <summary>Mede abertura, memória e animação e salva capturas. Ativado por --medir arquivo.json [--capturas pasta].</summary>
public sealed class RoteiroAutomatico(IJanelaPoc poc, string arquivoJson, string? pastaCapturas)
{
    private readonly Dictionary<string, object> _r = new();
    private readonly CatalogoPaletas _paletas = new();

    public async Task ExecutarAsync(double aberturaMs)
    {
        _r["biblioteca"] = poc.Biblioteca;
        _r["aberturaMs"] = Math.Round(aberturaMs);
        await Esperar(2000);
        _r["memoriaOciosa"] = new LeitorMemoria().Ler();
        var medidor = new MedidorAnimacao();
        medidor.Iniciar(); // linha de base: passes de layout com a tela parada
        await Esperar(300);
        _r["baseOciosa300ms"] = medidor.Parar();
        var ciclos = new List<ResultadoAnimacao>();
        for (var i = 0; i < 6; i++)
        {
            medidor.Iniciar();
            poc.Animador.Alternar();
            while (poc.Animador.Animando) await Esperar(16);
            await Esperar(50);
            ciclos.Add(medidor.Parar());
        }
        _r["animacaoRecolherExpandir"] = ciclos;
        await Rolar();
        _r["memoriaAposUso"] = new LeitorMemoria().Ler();
        if (pastaCapturas is not null) await Capturar();
        File.WriteAllText(arquivoJson, JsonSerializer.Serialize(_r, new JsonSerializerOptions { WriteIndented = true }));
        Application.Current.Shutdown();
    }

    private async Task Rolar()
    {
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < poc.Grade.Items.Count; i += 5) { poc.Grade.ScrollIntoView(poc.Grade.Items[i]); await Esperar(1); }
        _r["rolagemGradeMs"] = sw.ElapsedMilliseconds;
    }

    private async Task Capturar()
    {
        var cap = new Capturador();
        var tag = poc.Biblioteca.ToLowerInvariant().Replace(" ", "");
        poc.Grade.ScrollIntoView(poc.Grade.Items[0]);
        poc.Grade.UpdateLayout();
        await Esperar(300);
        var casos = new[] { (false, 0), (true, 0), (false, 3), (true, 3), (true, 6) };
        foreach (var (escuro, idx) in casos)
        {
            poc.AplicarTema(escuro, _paletas.Todas[idx]);
            await Esperar(700);
            cap.Salvar(poc.Janela, Path.Combine(pastaCapturas!, $"{tag}-{(escuro ? "escuro" : "claro")}-{_paletas.Todas[idx].Nome.ToLowerInvariant()}.png"));
        }
        foreach (var escuro in new[] { false, true })
        {
            poc.AplicarTema(escuro, _paletas.Todas[0]);
            poc.AbrirDialogo();
            await Esperar(900);
            cap.Salvar(poc.Janela, Path.Combine(pastaCapturas!, $"{tag}-{(escuro ? "escuro" : "claro")}-dialogo.png"));
            poc.FecharDialogo();
            await Esperar(400);
        }
        poc.Animador.Alternar();
        while (poc.Animador.Animando) await Esperar(16);
        poc.AplicarTema(false, _paletas.Todas[0]);
        await Esperar(500);
        cap.Salvar(poc.Janela, Path.Combine(pastaCapturas!, $"{tag}-claro-recolhida.png"));
    }

    private static async Task Esperar(int ms)
    {
        await Task.Delay(ms);
        await Dispatcher.Yield(DispatcherPriority.Render);
    }
}
