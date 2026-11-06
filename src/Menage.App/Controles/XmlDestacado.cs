using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Menage.App.Controles;

/// <summary>Mostra um trecho de XML com tags, atributos e valores em cores do tema (acompanha a troca de tema).</summary>
public sealed partial class XmlDestacado : TextBlock
{
    public static readonly DependencyProperty XmlProperty = DependencyProperty.Register(
        nameof(Xml), typeof(string), typeof(XmlDestacado), new PropertyMetadata("", (d, e) => ((XmlDestacado)d).Montar((string)e.NewValue)));

    [GeneratedRegex("""(?<tag></?[A-Za-z][\w:]*|/?>)|(?<attr>[A-Za-z][\w:.-]*)(?==)|(?<val>"[^"]*")""", RegexOptions.CultureInvariant)]
    private static partial Regex Padrao();

    public string Xml
    {
        get => (string)GetValue(XmlProperty);
        set => SetValue(XmlProperty, value);
    }

    void Montar(string texto)
    {
        Inlines.Clear();
        var fim = 0;
        foreach (Match m in Padrao().Matches(texto))
        {
            if (m.Index > fim) Inlines.Add(new Run(texto[fim..m.Index]));
            var tom = m.Groups["tag"].Success ? "Tema.Azul" : m.Groups["attr"].Success ? "Tema.Amarelo" : "Tema.Verde";
            var run = new Run(m.Value);
            run.SetResourceReference(TextElement.ForegroundProperty, tom);
            Inlines.Add(run);
            fim = m.Index + m.Length;
        }
        if (fim < texto.Length) Inlines.Add(new Run(texto[fim..]));
    }
}
