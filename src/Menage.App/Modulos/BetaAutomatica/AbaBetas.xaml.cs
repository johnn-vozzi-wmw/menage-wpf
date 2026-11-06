using System.Windows;
using System.Windows.Controls;

namespace Menage.App.Modulos.BetaAutomatica;

/// <summary>Aba Betas (tela D: lista + detalhe, barra do monitor e gavetas). Só liga a tela ao ViewModel.</summary>
public partial class AbaBetas : UserControl
{
    public AbaBetas() => InitializeComponent();

    /// <summary>As gavetas Ignoradas e Finalizadas abertas (a galeria usa para capturar).</summary>
    public void AbrirGavetas()
    {
        SecaoIgnoradas.IsExpanded = true;
        SecaoFinalizadas.IsExpanded = true;
    }

    public void FecharGavetas()
    {
        SecaoIgnoradas.IsExpanded = false;
        SecaoFinalizadas.IsExpanded = false;
    }

    void AoAbrirMais(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: { } menu } alvo) return;
        menu.PlacementTarget = alvo;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
