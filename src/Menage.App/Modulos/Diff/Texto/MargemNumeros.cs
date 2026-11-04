using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Menage.Core.Diff;

namespace Menage.App.Modulos.Diff.Texto;

/// <summary>
/// Margem com os números reais das linhas, a barra de cor da mudança e a marca de mudança invisível. Desenha com
/// GlyphRun: é refeita a cada quadro de rolagem, e um FormattedText por linha custava mais que o próprio texto.
/// </summary>
public sealed class MargemNumeros : AbstractMargin
{
    const double Recuo = 8;
    const double LarguraBarra = 3;

    readonly LinhasDoPainel _linhas;
    readonly CoresDiff _cores;
    readonly ushort[] _digitos = new ushort[10];
    GlyphTypeface? _fonte;
    double _tamanhoFonte;
    double _avanco;
    double _baseLinha;
    int _casas = 3;

    public MargemNumeros(LinhasDoPainel linhas, CoresDiff cores)
    {
        (_linhas, _cores) = (linhas, cores);
        ClipToBounds = true;
    }

    public void Configurar(FontFamily familia, double tamanho, int maiorNumero)
    {
        _tamanhoFonte = tamanho;
        new Typeface(familia, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out _fonte);
        if (_fonte is not null)
        {
            for (var i = 0; i < 10; i++) _digitos[i] = _fonte.CharacterToGlyphMap['0' + i];
            _avanco = Math.Round(_fonte.AdvanceWidths[_digitos[0]] * tamanho);
            _baseLinha = _fonte.Baseline * tamanho;
        }
        _casas = Math.Max(3, maiorNumero.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new((_casas * _avanco) + (2 * Recuo) + LarguraBarra, 0);

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null) oldTextView.VisualLinesChanged -= AoMudarLinhas;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) newTextView.VisualLinesChanged += AoMudarLinhas;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var vista = TextView;
        if (vista is not { VisualLinesValid: true } || _fonte is null) return;
        drawingContext.DrawRectangle(_cores.Fundo, null, new Rect(RenderSize));
        var pixelsPorDip = (float)VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var linha in vista.VisualLines)
        {
            var numeroDoc = linha.FirstDocumentLine.LineNumber;
            var y = linha.VisualTop - vista.VerticalOffset;
            var item = _linhas.Em(numeroDoc);
            if (item is not null) Marcas(drawingContext, item, y, linha.Height);
            var numero = _linhas.NumeroDe(numeroDoc);
            if (numero > 0) Numero(drawingContext, numero, y, linha.Height, pixelsPorDip);
        }
    }

    void AoMudarLinhas(object? sender, EventArgs e) => InvalidateVisual();

    void Marcas(DrawingContext dc, LinhaVista item, double y, double altura)
    {
        if (_cores.BarraDe(item.Tipo) is { } cor)
            dc.DrawRectangle(_cores.Pincel(cor), null, new Rect(ActualWidth - LarguraBarra, y, LarguraBarra, altura));
        if (item.Trivial != Trivial.Nao)
            dc.DrawEllipse(_cores.Pincel(_cores.Ambar), null, new Point(4, y + (altura / 2)), 2.5, 2.5);
    }

    void Numero(DrawingContext dc, int numero, double y, double altura, float pixelsPorDip)
    {
        var casas = numero < 10 ? 1 : (int)Math.Floor(Math.Log10(numero)) + 1;
        var glifos = new ushort[casas];
        var avancos = new double[casas];
        for (var i = casas - 1; i >= 0; i--, numero /= 10)
        {
            glifos[i] = _digitos[numero % 10];
            avancos[i] = _avanco;
        }
        var x = ActualWidth - LarguraBarra - Recuo - (casas * _avanco);
        var origem = new Point(Math.Round(x), Math.Round(y + ((altura - (_fonte!.Height * _tamanhoFonte)) / 2) + _baseLinha));
        var run = new GlyphRun(_fonte, 0, false, _tamanhoFonte, pixelsPorDip, glifos, origem, avancos, null, null, null, null, null, null);
        dc.DrawGlyphRun(_cores.TextoFraco, run);
    }
}
