using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Rendering;

namespace Menage.App.Modulos.Diff.Conflito;

public sealed record Pilula(string Texto, Action Acao, string? Dica = null);

/// <summary>
/// Botões no fim de algumas linhas. Moram no próprio texto, então acompanham a rolagem de graça, e só existem
/// nas linhas de conflito visíveis. Têm a altura e a linha de base do texto: uma linha mais alta quebraria a
/// conta de altura uniforme do mapa de rolagem.
/// </summary>
public sealed class PilulasDeLinha : VisualLineElementGenerator
{
    readonly TextView _vista;
    readonly Style _estilo;
    IReadOnlyDictionary<int, IReadOnlyList<Pilula>> _porLinha = new Dictionary<int, IReadOnlyList<Pilula>>();
    VisualLine? _feita;

    public PilulasDeLinha(TextView vista, Style estilo)
    {
        (_vista, _estilo) = (vista, estilo);
        vista.ElementGenerators.Add(this);
    }

    /// <summary>Linha do documento (base 1) → pílulas.</summary>
    public void Definir(IReadOnlyDictionary<int, IReadOnlyList<Pilula>> porLinha)
    {
        _porLinha = porLinha;
        _vista.Redraw(DispatcherPriority.Render);
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        var visual = CurrentContext.VisualLine;
        if (ReferenceEquals(_feita, visual) || !_porLinha.ContainsKey(visual.FirstDocumentLine.LineNumber)) return -1;
        var fim = visual.LastDocumentLine.EndOffset;
        return startOffset <= fim ? fim : -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        var visual = CurrentContext.VisualLine;
        _feita = visual;
        var painel = new StackPanel { Orientation = Orientation.Horizontal, Height = _vista.DefaultLineHeight, Margin = new Thickness(10, 0, 0, 0) };
        TextBlock.SetBaselineOffset(painel, _vista.DefaultBaseline);
        foreach (var pilula in _porLinha[visual.FirstDocumentLine.LineNumber])
        {
            var botao = new Button { Content = pilula.Texto, Style = _estilo, ToolTip = pilula.Dica, Focusable = false };
            botao.Click += (_, _) => pilula.Acao();
            painel.Children.Add(botao);
        }
        return new InlineObjectElement(0, painel);
    }
}
