using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Rendering;
using Menage.App.Modulos.Diff.Texto;
using Menage.Core.Diff;

namespace Menage.App.Modulos.Diff.Rolagem;

/// <summary>
/// Único dono da rolagem. Barras, roda, teclado, régua e navegação só mudam o ALVO; a cada quadro a posição chega
/// perto do alvo por tempo (decaimento exponencial: responde na hora, nunca passa do alvo nem volta) e todos os
/// painéis recebem seu deslocamento no mesmo callback. <c>InvalidateLayer(.., Render)</c> mede no próprio tique: o
/// <c>SetVerticalOffset</c> sozinho só agenda a medida, e a tela ficaria um quadro atrás da posição.
/// </summary>
public sealed class ControladorRolagem : IDisposable
{
    const double ConstanteSuaveMs = 55;
    const double ConstanteArrasteMs = 28;
    const int WmMouseHWheel = 0x020E;

    readonly IReadOnlyList<PainelTexto> _paineis;
    readonly IReadOnlyList<ScrollBar> _barrasV;
    readonly IReadOnlyList<ScrollBar> _barrasH;
    readonly double[] _y;
    readonly Stopwatch _relogio = Stopwatch.StartNew();
    Func<double, MapaRolagem>? _criarMapa;
    double _ultimoQuadroMs;
    double _constanteMs = ConstanteSuaveMs;
    double _alvoU;
    double _alvoX;
    double _maxX;
    bool _animando;
    bool _aplicando;
    HwndSource? _janela;

    public ControladorRolagem(IReadOnlyList<PainelTexto> paineis, IReadOnlyList<ScrollBar> barrasV, IReadOnlyList<ScrollBar> barrasH)
    {
        (_paineis, _barrasV, _barrasH) = (paineis, barrasV, barrasH);
        _y = new double[paineis.Count];
        foreach (var barra in barrasV) barra.Scroll += AoBarraV;
        foreach (var barra in barrasH) barra.Scroll += AoBarraH;
        foreach (var painel in paineis)
        {
            painel.Editor.PreviewMouseWheel += AoRoda;
            painel.Vista.ScrollOffsetChanged += AoMudarDeslocamento;
            if (!painel.Editavel) painel.Editor.TextArea.PreviewKeyDown += AoTecla;
        }
        paineis[0].Vista.SizeChanged += AoMudarTamanho;
    }

    /// <summary>Depois de mover os painéis, ainda no mesmo tique: faixas e régua se redesenham aqui.</summary>
    public event Action? Moveu;

    /// <summary>Custo (ms) de cada quadro aplicado, para a medição.</summary>
    public event Action<double>? QuadroAplicado;

    public MapaRolagem? Mapa { get; private set; }

    public double U { get; private set; }

    public double X { get; private set; }

    public double AlvoU => _alvoU;

    public bool Animando => _animando;

    /// <summary>Deixa um painel ser movido sozinho: só a medição por painel usa.</summary>
    public bool MedindoPainel { get; set; }

    public double Viewport => _paineis[0].Vista.ActualHeight;

    public double Y(int painel) => _y[painel];

    public void LigarJanela(Window janela)
    {
        _janela = (HwndSource)PresentationSource.FromVisual(janela);
        _janela.AddHook(AoMensagem);
    }

    public void LigarRoda(UIElement elemento) => elemento.PreviewMouseWheel += AoRoda;

    /// <summary>Troca o mapa mantendo a posição pedida. <paramref name="criar"/> recebe a altura da tela.</summary>
    public void DefinirMapa(Func<double, MapaRolagem> criar, double larguraTotal, Func<MapaRolagem, double>? posicao = null, double? x = null)
    {
        _criarMapa = criar;
        _maxX = Math.Max(0, larguraTotal - _paineis[0].Vista.ActualWidth);
        Mapa = criar(Viewport);
        AtualizarBarras();
        IrParaImediato(x ?? X, posicao?.Invoke(Mapa) ?? 0);
    }

    /// <summary>Anima até a posição. <paramref name="arrastando"/> acompanha mais de perto (barra e régua presas ao mouse).</summary>
    public void IrPara(double x, double u, bool arrastando = false)
    {
        if (Mapa is null) return;
        _constanteMs = arrastando ? ConstanteArrasteMs : ConstanteSuaveMs;
        (_alvoX, _alvoU) = (Math.Clamp(x, 0, _maxX), Math.Clamp(u, 0, Mapa.Maximo));
        if (_animando) return;
        _animando = true;
        _ultimoQuadroMs = _relogio.Elapsed.TotalMilliseconds;
        CompositionTarget.Rendering += AoQuadro;
    }

    public void IrParaImediato(double x, double u)
    {
        if (Mapa is null) return;
        Parar();
        (X, U) = (Math.Clamp(x, 0, _maxX), Math.Clamp(u, 0, Mapa.Maximo));
        (_alvoX, _alvoU) = (X, U);
        Aplicar();
    }

    public void Rolar(double dx, double du) => IrPara(_alvoX + dx, _alvoU + du);

    public void Dispose()
    {
        Parar();
        _janela?.RemoveHook(AoMensagem);
    }

    void Parar()
    {
        if (!_animando) return;
        _animando = false;
        CompositionTarget.Rendering -= AoQuadro;
    }

    void AoQuadro(object? sender, EventArgs e)
    {
        var agora = _relogio.Elapsed.TotalMilliseconds;
        var passou = agora - _ultimoQuadroMs;
        if (passou < 1) return; // o evento pode vir mais de uma vez no mesmo quadro
        _ultimoQuadroMs = agora;
        var k = 1 - Math.Exp(-passou / _constanteMs);
        U += (_alvoU - U) * k;
        X += (_alvoX - X) * k;
        var chegou = Math.Abs(_alvoU - U) < 0.5 && Math.Abs(_alvoX - X) < 0.5;
        if (chegou) (U, X) = (_alvoU, _alvoX);
        Aplicar();
        QuadroAplicado?.Invoke(_relogio.Elapsed.TotalMilliseconds - agora);
        if (chegou) Parar();
    }

    void Aplicar()
    {
        if (Mapa is null) return;
        _aplicando = true;
        var x = Math.Round(X);
        for (var i = 0; i < _paineis.Count; i++)
        {
            var painel = _paineis[i];
            _y[i] = Math.Round(Mapa.Y(i, U));
            painel.AjustarJanela(x);
            var rolagem = (IScrollInfo)painel.Vista;
            rolagem.SetHorizontalOffset(x);
            rolagem.SetVerticalOffset(_y[i]);
            painel.Vista.InvalidateLayer(KnownLayer.Text, DispatcherPriority.Render);
        }
        _aplicando = false;
        foreach (var barra in _barrasV)
        {
            if (!barra.IsMouseCaptureWithin) barra.Value = U;
        }
        foreach (var barra in _barrasH)
        {
            if (!barra.IsMouseCaptureWithin) barra.Value = X;
        }
        Moveu?.Invoke();
    }

    void AtualizarBarras()
    {
        var tela = Viewport;
        var largura = _paineis[0].Vista.ActualWidth;
        foreach (var barra in _barrasV)
        {
            (barra.Minimum, barra.Maximum, barra.ViewportSize) = (0, Mapa?.Maximo ?? 0, tela);
            (barra.LargeChange, barra.SmallChange) = (tela * 0.9, _paineis[0].AlturaLinha * 3);
        }
        foreach (var barra in _barrasH)
        {
            (barra.Minimum, barra.Maximum, barra.ViewportSize) = (0, _maxX, largura);
            (barra.LargeChange, barra.SmallChange) = (largura * 0.9, 40);
        }
    }

    void AoMudarTamanho(object sender, SizeChangedEventArgs e)
    {
        if (_criarMapa is null || Mapa is null) return;
        var fracao = Mapa.Maximo > 0 ? U / Mapa.Maximo : 0;
        _maxX = Math.Max(0, _maxX + e.PreviousSize.Width - e.NewSize.Width);
        Mapa = _criarMapa(Viewport);
        AtualizarBarras();
        IrParaImediato(X, fracao * Mapa.Maximo);
    }

    void AoBarraV(object sender, ScrollEventArgs e) =>
        IrPara(_alvoX, e.NewValue, e.ScrollEventType == ScrollEventType.ThumbTrack);

    void AoBarraH(object sender, ScrollEventArgs e) =>
        IrPara(e.NewValue, _alvoU, e.ScrollEventType == ScrollEventType.ThumbTrack);

    void AoRoda(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var passo = -e.Delta * (3 * _paineis[0].AlturaLinha / 120.0); // 3 linhas por "clique"; toques finos acumulam
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) Rolar(passo * 6, 0);
        else Rolar(0, passo);
    }

    IntPtr AoMensagem(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool tratado)
    {
        if (msg != WmMouseHWheel) return IntPtr.Zero;
        Rolar((short)((long)wParam >> 16) * 1.0, 0);
        tratado = true;
        return IntPtr.Zero;
    }

    void AoTecla(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return; // Shift seleciona
        var (h, tela) = (_paineis[0].AlturaLinha, Viewport);
        var largura = _paineis[0].Janela.LarguraCaractere;
        switch (e.Key)
        {
            case Key.PageDown: Rolar(0, tela * 0.9); break;
            case Key.PageUp: Rolar(0, -tela * 0.9); break;
            case Key.Down: Rolar(0, h); break;
            case Key.Up: Rolar(0, -h); break;
            case Key.Right: Rolar(largura * 4, 0); break;
            case Key.Left: Rolar(-largura * 4, 0); break;
            case Key.Home: IrPara(0, 0); break;
            case Key.End: IrPara(0, Mapa?.Maximo ?? 0); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>
    /// O próprio editor rolou (seleção arrastada para fora da tela, cursor no painel editável): a posição dele vira
    /// a posição de todos, na mesma mensagem. Sem foco no painel, foi só um ajuste interno, e a posição certa volta.
    /// </summary>
    void AoMudarDeslocamento(object? sender, EventArgs e)
    {
        if (_aplicando || MedindoPainel || Mapa is null) return;
        var i = IndiceDe((TextView)sender!);
        var vista = _paineis[i].Vista;
        if (Math.Abs(vista.VerticalOffset - _y[i]) < 1 && Math.Abs(vista.HorizontalOffset - Math.Round(X)) < 1) return;
        if (!_paineis[i].Editor.IsKeyboardFocusWithin)
        {
            Aplicar();
            return;
        }
        Parar();
        (X, U) = (vista.HorizontalOffset, Mapa.UDeY(i, vista.VerticalOffset));
        (_alvoX, _alvoU) = (X, U);
        Aplicar();
    }

    int IndiceDe(TextView vista)
    {
        for (var i = 0; i < _paineis.Count; i++)
        {
            if (ReferenceEquals(_paineis[i].Vista, vista)) return i;
        }
        return 0;
    }
}
