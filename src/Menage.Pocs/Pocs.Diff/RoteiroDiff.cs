using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Menage.Pocs.Comum;

namespace Menage.Pocs.Diff;

public record Estatistica(double MedianaMs, double P95Ms, double MaxMs, int Amostras);

/// <summary>Roteiro automático: mede o custo de pintura por painel, a sincronia do deslize e salva capturas.</summary>
public sealed class RoteiroDiff(MainWindow j, string saida, string? pastaCapturas)
{
    private readonly Dictionary<string, object> _r = new();

    public async Task ExecutarAsync()
    {
        _r["tempos"] = j.Tempos;
        await Esperar(1500);
        _r["memoriaAposCarregar"] = new LeitorMemoria().Ler();
        _r["trechosRecolhidos"] = j.Recolhimento.Trechos;
        _r["linhasAlinhadas"] = j.Modelo.Esquerda.Count;
        _r["alturaDocumentoPx_recolhido"] = Math.Round(j.Esquerdo.TextArea.TextView.DocumentHeight);

        await MedirModo(); // aquecimento (JIT) descartado
        _r["pinturaRecolhido"] = await MedirModo();
        _r["deslize"] = await MedirDeslize();
        if (pastaCapturas is not null) await Capturar();
        j.Recolhimento.DefinirTodos(false);
        await Esperar(500);
        _r["alturaDocumentoPx_expandido"] = Math.Round(j.Esquerdo.TextArea.TextView.DocumentHeight);
        _r["pinturaExpandido"] = await MedirModo();
        _r["memoriaAposUso"] = new LeitorMemoria().Ler();
        File.WriteAllText(saida, JsonSerializer.Serialize(_r, new JsonSerializerOptions { WriteIndented = true }));
        Application.Current.Shutdown();
    }

    /// <summary>Custo (UI thread: medida + layout + gravação do desenho) de cada passo, por painel e com os dois juntos.</summary>
    private async Task<Dictionary<string, Estatistica>> MedirModo()
    {
        var res = new Dictionary<string, Estatistica>();
        var vistas = j.Editores.Select(e => (IScrollInfo)e.TextArea.TextView).ToArray();
        var altura = j.Esquerdo.TextArea.TextView.DefaultLineHeight;
        var maxY = j.Esquerdo.TextArea.TextView.DocumentHeight - j.Esquerdo.TextArea.TextView.ActualHeight;
        var passoY = Math.Max(altura * 12, maxY / 80);
        string[] nomes = ["esquerdo", "direito"];

        for (var i = 0; i < 2; i++)
        {
            Zerar(vistas);
            await Esperar(200);
            res[$"vertical_{nomes[i]}"] = await Passos(80, k => vistas[i].SetVerticalOffset(Math.Min(maxY, (k + 1) * passoY)));
            Zerar(vistas);
            await Esperar(200);
            res[$"horizontal_{nomes[i]}"] = await Passos(80, k => { j.AjustarJanela((k + 1) * 250); vistas[i].SetHorizontalOffset((k + 1) * 250); });
        }
        Zerar(vistas);
        await Esperar(200);
        res["vertical_ambos"] = await Passos(80, k => j.Rolagem.IrParaImediato(0, Math.Min(maxY, (k + 1) * passoY)));
        j.Rolagem.IrParaImediato(0, 0);
        await Esperar(200);
        res["horizontal_ambos"] = await Passos(80, k => j.Rolagem.IrParaImediato((k + 1) * 250, 0));
        j.Rolagem.IrParaImediato(0, 0);
        return res;
    }

    private void Zerar(IScrollInfo[] v) { foreach (var x in v) { x.SetHorizontalOffset(0); x.SetVerticalOffset(0); } }

    private async Task<Estatistica> Passos(int n, Action<int> aplicar)
    {
        var ms = new List<double>();
        for (var k = 0; k < n; k++)
        {
            var sw = Stopwatch.StartNew();
            aplicar(k);
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            ms.Add(sw.Elapsed.TotalMilliseconds);
            await Task.Delay(8);
        }
        ms.Sort();
        return new(Math.Round(ms[ms.Count / 2], 2), Math.Round(ms[(int)(ms.Count * 0.95)], 2), Math.Round(ms[^1], 2), ms.Count);
    }

    /// <summary>Alvo distante + "arraste rápido" (alvo muda a cada 8 ms): confere sincronia entre painéis e passos por quadro.</summary>
    private async Task<object> MedirDeslize()
    {
        var quadros = new List<(double X, double Y, double Custo)>();
        var desalinhados = 0;
        string? primeiro = null;
        void Quadro(double custo)
        {
            var (a, b) = (j.Esquerdo.TextArea.TextView.ScrollOffset, j.Direito.TextArea.TextView.ScrollOffset);
            if (a != b) { desalinhados++; primeiro ??= $"esq={a} dir={b} atual={j.Rolagem.Atual}"; }
            quadros.Add((j.Rolagem.Atual.X, j.Rolagem.Atual.Y, custo));
        }
        j.Rolagem.QuadroAplicado += Quadro;
        j.Rolagem.IrParaImediato(0, 0);
        j.Rolagem.IrPara(6000, 30000);
        while (j.Rolagem.Animando) await Esperar(10);
        var simples = quadros.ToList();
        quadros.Clear();
        j.Rolagem.IrParaImediato(0, 0);
        await Esperar(100);
        for (var k = 1; k <= 60; k++) { j.Rolagem.IrPara(400 * k, 600 * k); await Task.Delay(8); }
        while (j.Rolagem.Animando) await Esperar(10);
        j.Rolagem.QuadroAplicado -= Quadro;
        j.Rolagem.IrParaImediato(0, 0);
        return new { alvoDistante = Resumir(simples), arrasteRapido = Resumir(quadros), paineisDesalinhados = desalinhados, primeiroDesalinhado = primeiro };
    }

    private static object Resumir(List<(double X, double Y, double Custo)> q)
    {
        double maxDx = 0, maxDy = 0;
        var recuos = 0;
        for (var i = 1; i < q.Count; i++)
        {
            maxDx = Math.Max(maxDx, Math.Abs(q[i].X - q[i - 1].X));
            maxDy = Math.Max(maxDy, Math.Abs(q[i].Y - q[i - 1].Y));
            if (q[i].X < q[i - 1].X - 0.01 || q[i].Y < q[i - 1].Y - 0.01) recuos++;
        }
        var custos = q.Select(x => x.Custo).OrderBy(x => x).ToList();
        return new
        {
            quadros = q.Count,
            maiorPassoXpx = Math.Round(maxDx, 1),
            maiorPassoYpx = Math.Round(maxDy, 1),
            recuos,
            custoQuadroMedianaMs = custos.Count > 0 ? Math.Round(custos[custos.Count / 2], 2) : 0,
            custoQuadroP95Ms = custos.Count > 0 ? Math.Round(custos[(int)(custos.Count * 0.95)], 2) : 0,
            custoQuadroMaxMs = custos.Count > 0 ? Math.Round(custos[^1], 2) : 0
        };
    }

    private async Task Capturar()
    {
        var cap = new Capturador();
        foreach (var escuro in new[] { false, true })
        {
            j.AplicarTema(escuro);
            j.Rolagem.IrParaImediato(0, 0);
            await Esperar(500);
            var nome = escuro ? "escuro" : "claro";
            cap.Salvar(j, Path.Combine(pastaCapturas!, $"diff-{nome}-inicio.png"));
            var (linha, x) = LinhaLongaAlterada();
            var tv = j.Esquerdo.TextArea.TextView;
            j.Rolagem.IrParaImediato(x, Math.Max(0, tv.GetVisualTopByDocumentLine(linha) - 5 * tv.DefaultLineHeight));
            await Esperar(500);
            cap.Salvar(j, Path.Combine(pastaCapturas!, $"diff-{nome}-linha-longa.png"));
        }
        j.AplicarTema(false);
        j.Rolagem.IrParaImediato(0, 0);
    }

    /// <summary>Primeira linha modificada e longa, e o X que mostra o fim dela (onde ficam as palavras trocadas).</summary>
    private (int Linha, double X) LinhaLongaAlterada()
    {
        var i = j.Modelo.Esquerda.Select((l, k) => (l, k)).First(t => t.l.Tipo == TipoLinha.Modificada && t.l.Texto.Length > 3000).k;
        var fim = j.Modelo.Esquerda[i].Palavras[^1].Fim;
        var larg = j.Esquerdo.TextArea.TextView.ActualWidth;
        return (i + 1, Math.Max(0, fim * (j.Rolagem.LarguraExtent - 40) / Math.Max(1, MaiorLinha()) - larg / 2));
    }

    private int MaiorLinha() => Math.Max(j.Modelo.Esquerda.Max(l => l.Texto.Length), j.Modelo.Direita.Max(l => l.Texto.Length));

    private static async Task Esperar(int ms)
    {
        await Task.Delay(ms);
        await Dispatcher.Yield(DispatcherPriority.Render);
    }
}
