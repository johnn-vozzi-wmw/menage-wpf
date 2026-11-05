using System.Windows;
using System.Windows.Input;

namespace Menage.App.Controles;

/// <summary>
/// Faz de um elemento que só reage ao mouse (cartão ou linha clicável) um item de teclado: entra na ordem do Tab e
/// Enter ou Espaço repetem o clique (<c>MouseLeftButtonUp</c>), então o mesmo manipulador atende mouse e teclado.
/// Uso: <c>c:AcessoPorTeclado.Ativar="True"</c> junto com <c>AutomationProperties.Name</c>.
/// </summary>
public static class AcessoPorTeclado
{
    public static readonly DependencyProperty AtivarProperty =
        DependencyProperty.RegisterAttached("Ativar", typeof(bool), typeof(AcessoPorTeclado), new PropertyMetadata(false, AoMudar));

    public static bool GetAtivar(DependencyObject d) => (bool)d.GetValue(AtivarProperty);

    public static void SetAtivar(DependencyObject d, bool valor) => d.SetValue(AtivarProperty, valor);

    static void AoMudar(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement elemento) return;
        elemento.KeyDown -= AoTeclar;
        if (e.NewValue is not true) return;
        elemento.Focusable = true;
        elemento.KeyDown += AoTeclar;
    }

    static void AoTeclar(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) || e.OriginalSource != sender || sender is not UIElement elemento) return;
        e.Handled = true;
        elemento.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent,
            Source = elemento,
        });
    }
}
