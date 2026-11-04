using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using ICSharpCode.AvalonEdit.Rendering;

namespace Menage.App.Modulos.Diff.Texto;

/// <summary>Faixa de colunas realmente formatada nas linhas longas; o resto vira espaço vazio.</summary>
public sealed class JanelaColunas
{
    public int Inicio { get; set; }

    public int Fim { get; set; }

    public int ColunasTotais { get; set; }

    public double LarguraCaractere { get; set; } = 7;

    /// <summary>De quantas em quantas linhas vai um espaçador: menos que as linhas da tela, para sempre haver um à vista.</summary>
    public int PassoEspacador { get; set; } = 4;

    /// <summary>Quantas vezes a janela foi refeita (medição).</summary>
    public int Refeitas { get; set; }
}

/// <summary>
/// Pedaço de linha que só ocupa largura. É um objeto de texto, não um elemento WPF: a prova usava um Border por
/// linha, e cada um custava um filho visual novo a cada quadro de rolagem.
/// </summary>
public sealed class ElementoVazio(int tamanhoNoDocumento, double largura) : VisualLineElement(1, tamanhoNoDocumento)
{
    public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context) =>
        new RunVazio(largura, TextRunProperties);

    sealed class RunVazio(double largura, TextRunProperties propriedades) : TextEmbeddedObject
    {
        public override LineBreakCondition BreakBefore => LineBreakCondition.BreakPossible;

        public override LineBreakCondition BreakAfter => LineBreakCondition.BreakPossible;

        public override bool HasFixedSize => true;

        public override CharacterBufferReference CharacterBufferReference => default;

        public override int Length => 1;

        public override TextRunProperties Properties => propriedades;

        public override TextEmbeddedObjectMetrics Format(double remainingParagraphWidth) => new(largura, 1, 1);

        public override Rect ComputeBoundingBox(bool rightToLeft, bool sideways) => new(0, 0, largura, 1);

        public override void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways)
        {
        }
    }
}

/// <summary>
/// Virtualização horizontal de linhas muito longas (fonte monoespaçada): fora da janela de colunas o texto vira
/// um bloco vazio com a mesma largura, então só ~5 larguras de tela são formatadas por linha.
/// </summary>
public sealed class FatiaLinhasLongas(JanelaColunas janela, int limite) : VisualLineElementGenerator
{
    public override int GetFirstInterestedOffset(int startOffset)
    {
        var linha = CurrentContext.VisualLine.FirstDocumentLine;
        if (linha.Length <= limite) return -1;
        var relativo = startOffset - linha.Offset;
        if (janela.Inicio > 0 && relativo <= 0) return linha.Offset;
        if (janela.Fim < linha.Length && relativo <= janela.Fim) return linha.Offset + janela.Fim;
        return -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var linha = CurrentContext.VisualLine.FirstDocumentLine;
        var esquerda = offset == linha.Offset && janela.Inicio > 0;
        var tamanho = esquerda ? Math.Min(janela.Inicio, linha.Length) : linha.Length - janela.Fim;
        return new ElementoVazio(tamanho, tamanho * janela.LarguraCaractere);
    }
}

/// <summary>
/// Põe no fim de uma linha por tela um espaço até a largura total. O AvalonEdit limita o deslocamento horizontal
/// à maior linha VISÍVEL de cada painel; sem isso os painéis parariam em X diferentes. Basta uma linha na tela.
/// </summary>
public sealed class EspacadorLargura(JanelaColunas janela) : VisualLineElementGenerator
{
    VisualLine? _feita;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        var linha = CurrentContext.VisualLine;
        if (ReferenceEquals(_feita, linha)) return -1;
        var ultima = linha.LastDocumentLine;
        return startOffset <= ultima.EndOffset && (linha.FirstDocumentLine.LineNumber - 1) % janela.PassoEspacador == 0 ? ultima.EndOffset : -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var linha = CurrentContext.VisualLine;
        _feita = linha;
        return new ElementoVazio(0, Math.Max(0, (janela.ColunasTotais - linha.FirstDocumentLine.Length) * janela.LarguraCaractere));
    }
}
