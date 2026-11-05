using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Menage.App.Modulos.Diff.Rolagem;

/// <summary>Marca na régua: fração do arquivo [Inicio, Inicio + Tamanho) e a cor de cada metade (nula = metade vazia).</summary>
public sealed record MarcaRegua(double Inicio, double Tamanho, Color? Esquerda, Color? Direita);

/// <summary>
/// Régua do arquivo inteiro. As marcas são desenhadas uma vez; o que anda na rolagem é só a transformação do
/// retângulo da tela. Clicar e arrastar levam o centro da tela até o mouse.
/// </summary>
public sealed class Regua : FrameworkElement
{
    readonly DrawingVisual _marcas = new();
    readonly DrawingVisual _janela = new();
    readonly TranslateTransform _posicao = new();
    IReadOnlyList<MarcaRegua> _lista = [];
    CoresDiff? _cores;
    (double Inicio, double Tamanho)? _foco;
    double _fracaoTela = 1;

    public Regua()
    {
        AddVisualChild(_marcas);
        AddVisualChild(_janela);
        _janela.Transform = _posicao;
        SizeChanged += (_, _) => Redesenhar();
    }

    /// <summary>Fração do arquivo sob o mouse e se ele está arrastando.</summary>
    public event Action<double, bool>? Rolou;

    protected override int VisualChildrenCount => 2;

    public void Configurar(CoresDiff cores, IReadOnlyList<MarcaRegua> marcas)
    {
        (_cores, _lista, _foco) = (cores, marcas, null);
        Redesenhar();
    }

    public void DefinirFoco(double inicio, double tamanho)
    {
        _foco = (inicio, tamanho);
        Redesenhar();
    }

    public void LimparFoco()
    {
        _foco = null;
        Redesenhar();
    }

    /// <summary>Posição da tela (frações do arquivo). Chamado a cada quadro: só move a transformação.</summary>
    public void Posicionar(double inicio, double tamanho)
    {
        if (Math.Abs(tamanho - _fracaoTela) > 0.0005)
        {
            _fracaoTela = tamanho;
            DesenharJanela();
        }
        _posicao.Y = inicio * ActualHeight;
    }

    protected override Visual GetVisualChild(int index) => index == 0 ? _marcas : _janela;

    protected override void OnRender(DrawingContext drawingContext) =>
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize)); // área inteira recebe o mouse

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        Rolou?.Invoke(Fracao(e), false);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) Rolou?.Invoke(Fracao(e), true);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => ReleaseMouseCapture();

    double Fracao(MouseEventArgs e) => ActualHeight > 0 ? Math.Clamp(e.GetPosition(this).Y / ActualHeight, 0, 1) : 0;

    void Redesenhar()
    {
        if (_cores is null) return;
        using (var dc = _marcas.RenderOpen())
        {
            var (w, h) = (ActualWidth, ActualHeight);
            dc.DrawRectangle(_cores.Fundo, null, new Rect(RenderSize));
            foreach (var marca in _lista) Marca(dc, marca, w, h);
            if (_foco is { } f) dc.DrawRectangle(_cores.Pincel(_cores.Destaque), null, new Rect(0, f.Inicio * h, w, Math.Max(3, f.Tamanho * h)));
        }
        DesenharJanela();
    }

    void Marca(DrawingContext dc, MarcaRegua marca, double w, double h)
    {
        var (y, altura) = (marca.Inicio * h, Math.Max(2, marca.Tamanho * h));
        if (marca.Esquerda is { } e) dc.DrawRectangle(_cores!.Pincel(e), null, new Rect(1, y, (w / 2) - 1, altura));
        if (marca.Direita is { } d) dc.DrawRectangle(_cores!.Pincel(d), null, new Rect(w / 2, y, (w / 2) - 1, altura));
    }

    void DesenharJanela()
    {
        if (_cores is null) return;
        using var dc = _janela.RenderOpen();
        var area = new Rect(0.5, 0, Math.Max(0, ActualWidth - 1), Math.Max(12, _fracaoTela * ActualHeight));
        var cor = (_cores.Texto as SolidColorBrush)?.Color ?? Colors.Gray;
        var fundo = new SolidColorBrush(Color.FromArgb(0x28, cor.R, cor.G, cor.B));
        var borda = new Pen(new SolidColorBrush(Color.FromArgb(0x70, cor.R, cor.G, cor.B)), 1);
        fundo.Freeze();
        borda.Freeze();
        dc.DrawRectangle(fundo, borda, area);
    }
}
