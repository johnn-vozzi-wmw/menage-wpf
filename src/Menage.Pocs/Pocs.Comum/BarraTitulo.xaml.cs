using System.Windows;
using System.Windows.Controls;

namespace Menage.Pocs.Comum;

/// <summary>Barra de título própria: título + minimizar/maximizar/fechar. Usar com ConfiguradorJanela.</summary>
public partial class BarraTitulo : UserControl
{
    public BarraTitulo()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Janela.StateChanged += (_, _) => AtualizarIcone();
            AtualizarIcone();
            TituloTexto.Text = Janela.Title;
        };
    }

    private Window Janela => Window.GetWindow(this)!;
    private void AtualizarIcone() => Maximizar.Content = Janela.WindowState == WindowState.Maximized ? "" : "";
    private void AoMinimizar(object s, RoutedEventArgs e) => Janela.WindowState = WindowState.Minimized;
    private void AoFechar(object s, RoutedEventArgs e) => Janela.Close();
    private void AoMaximizar(object s, RoutedEventArgs e) =>
        Janela.WindowState = Janela.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
