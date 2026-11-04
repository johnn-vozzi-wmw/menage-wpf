using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Menage.Pocs.Diff;

/// <summary>Cores de um tema do diff (linha inteira e palavra alterada, por tipo).</summary>
public record CoresDiff(Brush Inserida, Brush Removida, Brush Modificada, Brush PalavraNova, Brush PalavraVelha)
{
    public static CoresDiff Claro() => Criar("#E3F6E8", "#FBE4E4", "#FFF4D6", "#9EE0B0", "#F4A9A9");
    public static CoresDiff Escuro() => Criar("#17321F", "#3A1D1D", "#38311A", "#2F7A47", "#8E3A3A");

    private static CoresDiff Criar(params string[] hex)
    {
        Brush B(string h) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)); b.Freeze(); return b; }
        return new(B(hex[0]), B(hex[1]), B(hex[2]), B(hex[3]), B(hex[4]));
    }
}

/// <summary>Pinta o fundo da linha inteira (inclusive além do texto) conforme o tipo da linha do diff.</summary>
public sealed class FundoLinhaDiff(IReadOnlyList<LinhaDiff> linhas, Func<CoresDiff> cores) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (!textView.VisualLinesValid) return;
        var c = cores();
        var largura = Math.Max(textView.ActualWidth, textView.DocumentHeight > 0 ? textView.ActualWidth : 0) + textView.HorizontalOffset;
        foreach (var vl in textView.VisualLines)
        {
            var n = vl.FirstDocumentLine.LineNumber - 1;
            if (n >= linhas.Count) continue;
            var brush = linhas[n].Tipo switch
            {
                TipoLinha.Inserida => c.Inserida, TipoLinha.Removida => c.Removida, TipoLinha.Modificada => c.Modificada, _ => null
            };
            if (brush is null) continue;
            var y = vl.VisualTop - textView.VerticalOffset;
            dc.DrawRectangle(brush, null, new Rect(-textView.HorizontalOffset, y, largura, vl.Height));
        }
    }
}

/// <summary>Realça por palavra (só nas linhas visíveis) usando os trechos calculados pelo DiffPlex.</summary>
public sealed class PalavrasDiff(IReadOnlyList<LinhaDiff> linhas, Func<CoresDiff> cores, bool ladoNovo) : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        var n = line.LineNumber - 1;
        if (n >= linhas.Count || linhas[n].Palavras.Count == 0) return;
        var c = cores();
        var brush = ladoNovo ? c.PalavraNova : c.PalavraVelha;
        foreach (var t in linhas[n].Palavras)
            ChangeLinePart(line.Offset + t.Inicio, line.Offset + Math.Min(t.Fim, line.Length), e => e.TextRunProperties.SetBackgroundBrush(brush));
    }
}
