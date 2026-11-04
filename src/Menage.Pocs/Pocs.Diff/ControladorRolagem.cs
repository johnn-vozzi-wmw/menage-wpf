using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace Menage.Pocs.Diff;

/// <summary>
/// Único dono da rolagem dos dois painéis. As barras de rolagem, a roda, Shift+roda e o touchpad só mudam o
/// ALVO; a cada quadro (CompositionTarget.Rendering) a posição atual desliza por tempo até o alvo e os dois
/// editores recebem o mesmo deslocamento no mesmo callback, seguido de UpdateLayout nos dois — assim nenhum
/// painel pinta um quadro atrás do outro (não há aviso de rolagem pós-pintura).
/// </summary>
public sealed class ControladorRolagem
{
    private const double DuracaoMs = 150;
    private const int WM_MOUSEHWHEEL = 0x020E;
    private readonly TextEditor[] _editores;
    private readonly TextView[] _vistas;
    private readonly ScrollBar _barraV, _barraH;
    private readonly Stopwatch _relogio = Stopwatch.StartNew();
    private (double X, double Y) _atual, _alvo, _inicio;
    private double _inicioMs;
    private bool _animando;

    public double LarguraExtent { get; private set; } = 1000;
    public (double X, double Y) Atual => _atual;
    public bool Animando => _animando;
    public event Action<double>? QuadroAplicado;   // custo (ms) de cada quadro aplicado: usado pela medição
    public Action<double>? AntesDeAplicar { get; set; }  // recebe o X que será aplicado (para refazer a janela de colunas)

    public ControladorRolagem(TextEditor esquerdo, TextEditor direito, ScrollBar barraV, ScrollBar barraH)
    {
        _editores = [esquerdo, direito];
        _vistas = [esquerdo.TextArea.TextView, direito.TextArea.TextView];
        (_barraV, _barraH) = (barraV, barraH);
        barraV.Scroll += (_, e) => DefinirAlvo(_alvo.X, e.NewValue);
        barraH.Scroll += (_, e) => DefinirAlvo(e.NewValue, _alvo.Y);
        foreach (var ed in _editores) ed.PreviewMouseWheel += AoRoda;
        _vistas[0].VisualLinesChanged += (_, _) => AtualizarLimites();
        _vistas[0].SizeChanged += (_, _) => AtualizarLimites();
    }

    public void DefinirLarguraExtent(double largura) { LarguraExtent = largura; AtualizarLimites(); }

    public void LigarTouchpad(Window janela) =>
        ((HwndSource)PresentationSource.FromVisual(janela)).AddHook(AoMensagem);

    public void IrPara(double x, double y) => DefinirAlvo(x, y);

    /// <summary>Aplica já (sem deslizar): usado pelo roteiro de medição.</summary>
    public void IrParaImediato(double x, double y)
    {
        (_atual, _alvo) = ((x, y), (x, y));
        Aplicar();
    }

    private void AtualizarLimites()
    {
        var vp = _vistas[0].ActualHeight;
        _barraV.ViewportSize = vp;
        _barraV.Maximum = Math.Max(0, _vistas[0].DocumentHeight - vp);
        var vw = _vistas[0].ActualWidth;
        _barraH.ViewportSize = vw;
        _barraH.Maximum = Math.Max(0, LarguraExtent - vw);
        _barraV.LargeChange = vp * 0.9;
        _barraH.LargeChange = vw * 0.9;
    }

    private void AoRoda(object s, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var passo = -e.Delta * (3 * _vistas[0].DefaultLineHeight / 120.0); // 3 linhas por "clique"; toques finos acumulam
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) DefinirAlvo(_alvo.X + passo * 6, _alvo.Y);
        else DefinirAlvo(_alvo.X, _alvo.Y + passo);
    }

    private IntPtr AoMensagem(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool tratado)
    {
        if (msg != WM_MOUSEHWHEEL) return IntPtr.Zero;
        var delta = (short)((long)w >> 16);
        DefinirAlvo(_alvo.X + delta * (40.0 / 120.0) * 3, _alvo.Y);
        tratado = true;
        return IntPtr.Zero;
    }

    private void DefinirAlvo(double x, double y)
    {
        _alvo = (Math.Clamp(x, 0, _barraH.Maximum), Math.Clamp(y, 0, _barraV.Maximum));
        _inicio = _atual;
        _inicioMs = _relogio.Elapsed.TotalMilliseconds;
        if (_animando) return;
        _animando = true;
        CompositionTarget.Rendering += AoQuadro;
    }

    private void AoQuadro(object? s, EventArgs e)
    {
        var t = Math.Min(1, (_relogio.Elapsed.TotalMilliseconds - _inicioMs) / DuracaoMs);
        var k = 1 - Math.Pow(1 - t, 3); // ease-out cúbico, por TEMPO
        _atual = (_inicio.X + (_alvo.X - _inicio.X) * k, _inicio.Y + (_alvo.Y - _inicio.Y) * k);
        var sw = Stopwatch.StartNew();
        Aplicar();
        QuadroAplicado?.Invoke(sw.Elapsed.TotalMilliseconds);
        if (t < 1) return;
        _animando = false;
        CompositionTarget.Rendering -= AoQuadro;
    }

    private void Aplicar()
    {
        AntesDeAplicar?.Invoke(_atual.X);
        foreach (var v in _vistas)
        {
            var info = (IScrollInfo)v;
            info.SetHorizontalOffset(_atual.X);
            info.SetVerticalOffset(_atual.Y);
        }
        foreach (var ed in _editores) ed.UpdateLayout();
        if (!_barraV.IsMouseCaptureWithin) _barraV.Value = _atual.Y;
        if (!_barraH.IsMouseCaptureWithin) _barraH.Value = _atual.X;
    }
}
