using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Menage.App.Controles;

/// <summary>Filtro ligável; com <see cref="Removivel"/>, mostra um × que dispara <see cref="Removido"/>.</summary>
public sealed class Chip : ToggleButton
{
    public static readonly DependencyProperty TextoProperty =
        DependencyProperty.Register(nameof(Texto), typeof(string), typeof(Chip), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty RemovivelProperty =
        DependencyProperty.Register(nameof(Removivel), typeof(bool), typeof(Chip));

    public static readonly DependencyProperty CorProperty =
        DependencyProperty.Register(nameof(Cor), typeof(Brush), typeof(Chip));

    public static readonly DependencyProperty TomProperty = DependencyProperty.Register(
        nameof(Tom), typeof(TomDeCor), typeof(Chip),
        new PropertyMetadata(TomDeCor.Destaque, (d, e) => TonsDeCor.Ligar((FrameworkElement)d, CorProperty, (TomDeCor)e.NewValue)));

    public static readonly RoutedEvent RemovidoEvent =
        EventManager.RegisterRoutedEvent(nameof(Removido), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Chip));

    public Chip() => TonsDeCor.Ligar(this, CorProperty, TomDeCor.Destaque);

    public event RoutedEventHandler Removido
    {
        add => AddHandler(RemovidoEvent, value);
        remove => RemoveHandler(RemovidoEvent, value);
    }

    public string Texto
    {
        get => (string)GetValue(TextoProperty);
        set => SetValue(TextoProperty, value);
    }

    public bool Removivel
    {
        get => (bool)GetValue(RemovivelProperty);
        set => SetValue(RemovivelProperty, value);
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

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Remover") is ButtonBase remover)
        {
            remover.Click += (_, e) =>
            {
                e.Handled = true;
                RaiseEvent(new RoutedEventArgs(RemovidoEvent, this));
            };
        }
    }
}
