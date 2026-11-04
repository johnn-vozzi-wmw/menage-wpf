using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Rendering;
using Menage.App.Modulos.Diff;
using Menage.App.Modulos.Diff.Texto;

namespace Menage.Diff.Roteiro;

public sealed record Estatistica(double MedianaMs, double P95Ms, double MaxMs, int Amostras)
{
    public static Estatistica De(List<double> valores)
    {
        if (valores.Count == 0) return new(0, 0, 0, 0);
        var ordem = valores.OrderBy(v => v).ToList();
        return new(Math.Round(ordem[ordem.Count / 2], 2), Math.Round(ordem[(int)(ordem.Count * 0.95)], 2), Math.Round(ordem[^1], 2), ordem.Count);
    }
}

/// <summary>Custo de pintura por painel, com o mesmo método da prova (thread de UI até o ciclo de renderização terminar) e também só a medida+layout.</summary>
public sealed class MedicaoPintura(JanelaDiff janela)
{
    readonly PainelDiff _p = janela.PainelDoDiff;

    public async Task<object> RodarAsync()
    {
        (janela.Width, janela.Height) = (1300, 760); // mesmo tamanho da prova de conceito
        await Execucao.Esperar(600);
        await MedirModo(); // aquecimento (JIT), descartado
        var recolhido = await MedirModo();
        var horizontal = await MedirHorizontal();
        _p.DefinirChaves(recolher: false, ignorar: true);
        await Execucao.Esperar(800);
        var expandido = await MedirModo();
        _p.DefinirChaves(recolher: true, ignorar: true);
        await Execucao.Esperar(500);
        return new { recolhido, expandido, horizontal, memoriaMb = GC.GetTotalMemory(false) / 1024 / 1024 };
    }

    async Task<Dictionary<string, object>> MedirModo()
    {
        var r = new Dictionary<string, object>();
        foreach (var (nome, painel) in new[] { ("esquerdo", _p.Esquerdo), ("direito", _p.Direito) })
        {
            _p.Rolagem.IrParaImediato(0, 0);
            await Execucao.Esperar(150);
            _p.Rolagem.MedindoPainel = true;
            var altura = painel.Vista.DocumentHeight - painel.Vista.ActualHeight;
            var passo = Math.Max(painel.AlturaLinha * 12, altura / 80);
            r[$"vertical_{nome}"] = await Passos(80, k => Vertical(painel, Math.Min(altura, (k + 1) * passo)));
            r[$"vertical_{nome}_layout"] = await SoLayout(80, painel, k => Vertical(painel, Math.Min(altura, (k + 1) * passo)));
            _p.Rolagem.MedindoPainel = false;
        }
        _p.Rolagem.IrParaImediato(0, 0);
        await Execucao.Esperar(150);
        var maximo = _p.Rolagem.Mapa!.Maximo;
        r["vertical_ambos"] = await Passos(80, k => _p.Rolagem.IrParaImediato(0, (k + 1) * maximo / 80));
        _p.Rolagem.IrParaImediato(0, 0);
        return r;
    }

    static void Vertical(PainelTexto painel, double y)
    {
        ((IScrollInfo)painel.Vista).SetVerticalOffset(y);
        painel.Vista.InvalidateLayer(KnownLayer.Text, DispatcherPriority.Render);
    }

    /// <summary>Saltos horizontais com a janela de colunas sendo refeita; os maiores valores são as refeitas.</summary>
    async Task<object> MedirHorizontal()
    {
        _p.Rolagem.IrParaImediato(0, _p.Rolagem.Mapa!.Maximo * 0.3);
        await Execucao.Esperar(200);
        var gen0 = GC.CollectionCount(0);
        var longasNaTela = _p.Direito.Vista.VisualLines.Count(l => l.FirstDocumentLine.Length > PainelTexto.LimiteLinhaLonga);
        var (comRefeita, semRefeita) = (new List<double>(), new List<double>());
        var relogio = new Stopwatch();
        for (var k = 1; k <= 120; k++)
        {
            var antes = _p.Direito.Janela.Refeitas;
            relogio.Restart();
            _p.Rolagem.IrParaImediato(k * 250, _p.Rolagem.U);
            _p.Direito.Vista.UpdateLayout();
            (_p.Direito.Janela.Refeitas != antes ? comRefeita : semRefeita).Add(relogio.Elapsed.TotalMilliseconds);
            await Task.Delay(16);
        }
        var rapido = await Passos(60, k => _p.Rolagem.IrParaImediato((k + 1) * 1000, _p.Rolagem.U));
        _p.Rolagem.IrParaImediato(0, 0);
        return new { longasNaTela, nivelRender = System.Windows.Media.RenderCapability.Tier >> 16, colecoesGen0 = GC.CollectionCount(0) - gen0, comRefeita = Estatistica.De(comRefeita), semRefeita = Estatistica.De(semRefeita), rapido };
    }

    static async Task<Estatistica> Passos(int n, Action<int> aplicar)
    {
        var ms = new List<double>();
        for (var k = 0; k < n; k++)
        {
            var relogio = Stopwatch.StartNew();
            aplicar(k);
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            ms.Add(relogio.Elapsed.TotalMilliseconds);
            await Task.Delay(8);
        }
        return Estatistica.De(ms);
    }

    /// <summary>Só o trabalho do painel: da mudança de posição até o fim do layout, sem a espera do despachante.</summary>
    static async Task<Estatistica> SoLayout(int n, PainelTexto painel, Action<int> aplicar)
    {
        var ms = new List<double>();
        for (var k = 0; k < n; k++)
        {
            var relogio = Stopwatch.StartNew();
            aplicar(k);
            painel.Vista.UpdateLayout();
            ms.Add(relogio.Elapsed.TotalMilliseconds);
            await Task.Delay(16);
        }
        return Estatistica.De(ms);
    }
}
