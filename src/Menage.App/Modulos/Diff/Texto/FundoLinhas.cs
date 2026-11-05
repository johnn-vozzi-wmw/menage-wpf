using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Menage.Core.Diff;

namespace Menage.App.Modulos.Diff.Texto;

/// <summary>
/// Fundo da linha inteira por tipo e, por cima, o realce da mudança atual. O realce usa um pincel animado: a
/// animação do pulso roda sozinha na composição, sem refazer esta camada a cada quadro.
/// </summary>
public sealed class FundoLinhas(LinhasDoPainel linhas, CoresDiff cores) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Background;

    /// <summary>Linhas realçadas [inicio, fim), base 1; nulo sem realce.</summary>
    public (int Inicio, int Fim)? Realce { get; set; }

    public Brush? PincelRealce { get; set; }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid) return;
        foreach (var linha in textView.VisualLines)
        {
            var numero = linha.FirstDocumentLine.LineNumber;
            var area = new Rect(0, linha.VisualTop - textView.VerticalOffset, textView.ActualWidth, linha.Height);
            if (linhas.Em(numero) is { } item && cores.FundoDe(item.Tipo) is { } fundo) drawingContext.DrawRectangle(fundo, null, area);
            if (Realce is { } r && numero >= r.Inicio && numero < r.Fim && PincelRealce is not null) drawingContext.DrawRectangle(PincelRealce, null, area);
        }
    }
}

/// <summary>Realça por palavra e deixa a linha de trecho recolhido com a cor de link. Só roda nas linhas construídas.</summary>
public sealed class PalavrasAlteradas(LinhasDoPainel linhas, CoresDiff cores, bool ladoNovo) : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        if (linhas.Em(line.LineNumber) is not { } item) return;
        if (item.Tipo is TipoLinha.Dobra or TipoLinha.Conflito)
        {
            var pincel = item.Tipo == TipoLinha.Dobra ? cores.Pincel(cores.Destaque) : cores.Texto;
            ChangeLinePart(line.Offset, line.EndOffset, e => e.TextRunProperties.SetForegroundBrush(pincel));
            return;
        }
        var fundo = ladoNovo ? cores.PalavraNova : cores.PalavraVelha;
        foreach (var t in item.Palavras)
        {
            var (inicio, fim) = (line.Offset + t.Inicio, line.Offset + Math.Min(t.Inicio + t.Tamanho, line.Length));
            if (inicio < fim) ChangeLinePart(inicio, fim, e => e.TextRunProperties.SetBackgroundBrush(fundo));
        }
    }
}

/// <summary>Etiqueta no fim das linhas que mudaram só no invisível ("fim de linha LF → CRLF"), com a área para a dica.</summary>
public sealed class EtiquetasDeLinha(LinhasDoPainel linhas, CoresDiff cores) : IBackgroundRenderer
{
    readonly Dictionary<string, FormattedText> _textos = [];
    readonly List<(Rect Area, string Nota)> _areas = [];

    public KnownLayer Layer => KnownLayer.Background;

    public string? NotaEm(Point ponto) => _areas.FirstOrDefault(a => a.Area.Contains(ponto)).Nota;

    public void Limpar() => _textos.Clear();

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        _areas.Clear();
        if (!textView.VisualLinesValid) return;
        foreach (var linha in textView.VisualLines)
        {
            if (linhas.Em(linha.FirstDocumentLine.LineNumber) is not { Nota: { } nota, SoDica: false }) continue;
            var fim = linha.GetVisualColumn(linha.FirstDocumentLine.Length);
            var x = linha.GetTextLineVisualXPosition(linha.TextLines[0], fim) - textView.HorizontalOffset + 12;
            var y = linha.VisualTop - textView.VerticalOffset;
            var texto = Texto(nota, textView);
            var area = new Rect(x, y + 1, texto.Width + 12, linha.Height - 2);
            drawingContext.DrawRoundedRectangle(cores.EtiquetaFundo, null, area, 4, 4);
            drawingContext.DrawText(texto, new Point(x + 6, y + ((linha.Height - texto.Height) / 2)));
            _areas.Add((area, nota));
        }
    }

    FormattedText Texto(string nota, TextView vista)
    {
        if (_textos.TryGetValue(nota, out var pronto)) return pronto;
        var tipo = new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var dpi = VisualTreeHelper.GetDpi(vista).PixelsPerDip;
        return _textos[nota] = new FormattedText(nota, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tipo, 11, cores.Texto, dpi);
    }
}
