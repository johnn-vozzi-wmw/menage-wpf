using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Menage.Core.BetaAutomatica;

namespace Menage.App.Modulos.BetaAutomatica;

/// <summary>TextBlock que mostra os trechos de uma JQL, destacando os marcadores <c>{projeto}</c>. As cores vêm do tema (acompanham a troca).</summary>
public sealed class TextoComMarcadores : TextBlock
{
    public static readonly DependencyProperty TrechosProperty = DependencyProperty.Register(
        nameof(Trechos), typeof(IReadOnlyList<TrechoDeJql>), typeof(TextoComMarcadores), new PropertyMetadata(null, (d, _) => ((TextoComMarcadores)d).Montar()));

    public IReadOnlyList<TrechoDeJql>? Trechos
    {
        get => (IReadOnlyList<TrechoDeJql>?)GetValue(TrechosProperty);
        set => SetValue(TrechosProperty, value);
    }

    void Montar()
    {
        Inlines.Clear();
        foreach (var t in Trechos ?? [])
        {
            var run = new Run(t.Texto);
            if (t.Marcador)
            {
                run.SetResourceReference(TextElement.ForegroundProperty, "Tema.Azul");
                run.SetResourceReference(TextElement.BackgroundProperty, "Tema.GradeSel");
                run.FontWeight = FontWeights.SemiBold;
            }

            Inlines.Add(run);
        }
    }
}
