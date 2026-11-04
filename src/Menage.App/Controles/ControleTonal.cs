using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Menage.App.Controles;

/// <summary>Base dos rótulos coloridos: texto, tom do tema ou cor livre em <see cref="Cor"/>.</summary>
public abstract class ControleTonal : Control
{
    public static readonly DependencyProperty TextoProperty =
        DependencyProperty.Register(nameof(Texto), typeof(string), typeof(ControleTonal), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CorProperty =
        DependencyProperty.Register(nameof(Cor), typeof(Brush), typeof(ControleTonal));

    public static readonly DependencyProperty TomProperty = DependencyProperty.Register(
        nameof(Tom), typeof(TomDeCor), typeof(ControleTonal),
        new PropertyMetadata(TomDeCor.Destaque, (d, e) => TonsDeCor.Ligar((FrameworkElement)d, CorProperty, (TomDeCor)e.NewValue)));

    protected ControleTonal() => TonsDeCor.Ligar(this, CorProperty, TomDeCor.Destaque);

    public string Texto
    {
        get => (string)GetValue(TextoProperty);
        set => SetValue(TextoProperty, value);
    }

    public TomDeCor Tom
    {
        get => (TomDeCor)GetValue(TomProperty);
        set => SetValue(TomProperty, value);
    }

    public Brush? Cor
    {
        get => (Brush?)GetValue(CorProperty);
        set => SetValue(CorProperty, value);
    }
}

/// <summary>Ficha arredondada com texto em negrito: contador ("Mudança 3 de 10"), estado de um conflito.</summary>
public sealed class Ficha : ControleTonal;

/// <summary>Etiqueta compacta para células de grade (grupo, situação). Leve: usada em linhas virtualizadas.</summary>
public sealed class Etiqueta : ControleTonal;

public enum TipoDeSelo { Informacao, Aviso, Erro, Pergunta }

/// <summary>Círculo colorido com i, !, × ou ? das mensagens.</summary>
public sealed class Selo : ControleTonal
{
    public static readonly DependencyProperty TipoProperty = DependencyProperty.Register(
        nameof(Tipo), typeof(TipoDeSelo), typeof(Selo),
        new PropertyMetadata(TipoDeSelo.Informacao, (d, _) => ((Selo)d).Atualizar()));

    public Selo() => Atualizar();

    public TipoDeSelo Tipo
    {
        get => (TipoDeSelo)GetValue(TipoProperty);
        set => SetValue(TipoProperty, value);
    }

    void Atualizar() => (Texto, Tom) = Tipo switch
    {
        TipoDeSelo.Erro => ("×", TomDeCor.Vermelho),
        TipoDeSelo.Aviso => ("!", TomDeCor.Amarelo),
        TipoDeSelo.Pergunta => ("?", TomDeCor.Destaque),
        _ => ("i", TomDeCor.Destaque),
    };
}
