using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace Menage.App.Controles;

/// <summary>
/// Botão de ação em cápsula. <see cref="Compacta"/> deixa só o ícone (o texto vira dica); quem liga é a
/// <see cref="BarraDePilulas"/>. Com <see cref="Menu"/>, ganha seta e abre o menu logo abaixo ao clicar.
/// </summary>
public sealed class Pilula : System.Windows.Controls.Button
{
    public static readonly DependencyProperty TextoProperty = DependencyProperty.Register(
        nameof(Texto), typeof(string), typeof(Pilula),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsParentMeasure, (d, _) => ((Pilula)d).AvisarBarra()));

    public static readonly DependencyProperty IconeProperty = DependencyProperty.Register(
        nameof(Icone), typeof(SymbolRegular), typeof(Pilula), new PropertyMetadata(SymbolRegular.Empty));

    public static readonly DependencyProperty IdDoIconeProperty =
        DependencyProperty.Register(nameof(IdDoIcone), typeof(string), typeof(Pilula));

    public static readonly DependencyProperty CorProperty =
        DependencyProperty.Register(nameof(Cor), typeof(Brush), typeof(Pilula));

    public static readonly DependencyProperty TomProperty = DependencyProperty.Register(
        nameof(Tom), typeof(TomDeCor), typeof(Pilula),
        new PropertyMetadata(TomDeCor.Neutro, (d, e) => TonsDeCor.Ligar((FrameworkElement)d, CorProperty, (TomDeCor)e.NewValue)));

    public static readonly DependencyProperty PreenchidaProperty =
        DependencyProperty.Register(nameof(Preenchida), typeof(bool), typeof(Pilula));

    public static readonly DependencyProperty CompactaProperty = DependencyProperty.Register(
        nameof(Compacta), typeof(bool), typeof(Pilula),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => InvalidarTemplate((Pilula)d)));

    public static readonly DependencyProperty MenuProperty =
        DependencyProperty.Register(nameof(Menu), typeof(ContextMenu), typeof(Pilula));

    public Pilula() => TonsDeCor.Ligar(this, CorProperty, TomDeCor.Neutro);

    public string Texto
    {
        get => (string)GetValue(TextoProperty);
        set => SetValue(TextoProperty, value);
    }

    public SymbolRegular Icone
    {
        get => (SymbolRegular)GetValue(IconeProperty);
        set => SetValue(IconeProperty, value);
    }

    /// <summary>Chave do ícone personalizado (id do comando principal ou <c>pilula:Texto</c>).</summary>
    public string? IdDoIcone
    {
        get => (string?)GetValue(IdDoIconeProperty);
        set => SetValue(IdDoIconeProperty, value);
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

    /// <summary>Fundo na cor do tom com texto branco: a ação principal da barra.</summary>
    public bool Preenchida
    {
        get => (bool)GetValue(PreenchidaProperty);
        set => SetValue(PreenchidaProperty, value);
    }

    public bool Compacta
    {
        get => (bool)GetValue(CompactaProperty);
        set => SetValue(CompactaProperty, value);
    }

    public ContextMenu? Menu
    {
        get => (ContextMenu?)GetValue(MenuProperty);
        set => SetValue(MenuProperty, value);
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (Menu is not { } menu) return;
        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // Compacta, a pílula só mostra o ícone: o leitor de tela e o Tab precisam do nome mesmo assim.
    void AvisarBarra()
    {
        System.Windows.Automation.AutomationProperties.SetName(this, Texto);
        (Parent as BarraDePilulas)?.EsquecerLarguras();
    }

    // A barra mede a pílula na hora, dentro do próprio Measure: sem isto os elementos do meio do template
    // devolveriam o tamanho antigo, porque só o rótulo escondido ficaria marcado para medir de novo.
    static void InvalidarTemplate(Pilula pilula)
    {
        foreach (var elemento in Visuais.Descendentes(pilula).OfType<UIElement>()) elemento.InvalidateMeasure();
    }
}
