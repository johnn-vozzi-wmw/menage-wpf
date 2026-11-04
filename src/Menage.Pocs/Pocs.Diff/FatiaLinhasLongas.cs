using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Rendering;

namespace Menage.Pocs.Diff;

/// <summary>Faixa de colunas (caracteres) realmente formatada nas linhas longas; o resto vira espaço vazio.</summary>
public sealed class JanelaColunas
{
    public int Inicio { get; set; }
    public int Fim { get; set; }
    public int ColunasTotais { get; set; }
}

/// <summary>
/// Virtualização horizontal de linhas muito longas (fonte monoespaçada): fora da janela de colunas o texto é
/// trocado por um bloco vazio com a mesma largura, então o AvalonEdit só formata ~5 larguras de tela por linha
/// em vez de milhares de caracteres. A janela só é refeita quando a rolagem chega perto da borda dela.
/// </summary>
public sealed class FatiaLinhasLongas(JanelaColunas janela, double larguraChar, int limite = 1200) : VisualLineElementGenerator
{
    public override int GetFirstInterestedOffset(int startOffset)
    {
        var linha = CurrentContext.VisualLine.FirstDocumentLine;
        if (linha.Length <= limite) return -1;
        var rel = startOffset - linha.Offset;
        if (janela.Inicio > 0 && rel <= 0) return linha.Offset;
        if (janela.Fim < linha.Length && rel <= janela.Fim) return linha.Offset + janela.Fim;
        return -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var linha = CurrentContext.VisualLine.FirstDocumentLine;
        var esquerda = offset == linha.Offset && janela.Inicio > 0;
        var tamanho = esquerda ? Math.Min(janela.Inicio, linha.Length) : linha.Length - janela.Fim;
        return new InlineObjectElement(tamanho, new Border { Width = tamanho * larguraChar, Height = 1 });
    }
}

/// <summary>
/// Iguala a largura de todas as linhas visuais (texto + espaçador no fim) à da linha mais longa. Sem isso o
/// AvalonEdit limita o deslocamento horizontal à maior linha VISÍVEL de cada painel, e os dois painéis poderiam
/// parar em X diferentes (um clamp diferente do outro) quando as linhas visíveis têm larguras diferentes.
/// </summary>
public sealed class EqualizadorLargura(JanelaColunas janela, double larguraChar) : VisualLineElementGenerator
{
    private VisualLine? _feita;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        var linha = CurrentContext.VisualLine;
        if (ReferenceEquals(_feita, linha)) return -1;
        var ultima = linha.LastDocumentLine;
        // só 1 de cada 4 linhas leva o espaçador (basta 1 linha visível com a largura total; reduz o custo por linha)
        return startOffset <= ultima.EndOffset && linha.FirstDocumentLine.LineNumber % 4 == 0 ? ultima.EndOffset : -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var linha = CurrentContext.VisualLine;
        _feita = linha;
        var largura = Math.Max(0, (janela.ColunasTotais - linha.FirstDocumentLine.Length) * larguraChar);
        return new InlineObjectElement(0, new Border { Width = largura, Height = 1 });
    }
}
