using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Menage.Core.Busca;

namespace Menage.App.Controles;

/// <summary>
/// Mostra uma <see cref="Sugestao"/> com os trechos digitados em destaque (cor de destaque do tema e negrito).
/// Reconstrói só os <c>Run</c> quando o contexto muda, o que combina com a reciclagem da lista virtualizada.
/// </summary>
public sealed class TextoDestacado : TextBlock
{
    public TextoDestacado()
    {
        TextTrimming = TextTrimming.CharacterEllipsis;
        DataContextChanged += (_, _) => Montar();
    }

    void Montar()
    {
        Inlines.Clear();
        if (DataContext is not Sugestao s) return;

        var pos = 0;
        foreach (var t in s.Destaques)
        {
            if (t.Inicio > pos) Inlines.Add(new Run(s.Texto[pos..t.Inicio]));
            var run = new Run(s.Texto.Substring(t.Inicio, t.Tamanho)) { FontWeight = FontWeights.SemiBold };
            run.SetResourceReference(TextElement.ForegroundProperty, "Tema.Destaque");
            Inlines.Add(run);
            pos = t.Inicio + t.Tamanho;
        }

        if (pos < s.Texto.Length) Inlines.Add(new Run(s.Texto[pos..]));
    }
}
