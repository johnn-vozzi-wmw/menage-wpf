using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Menage.App.Modulos.BetaAutomatica;

/// <summary>Aba Configurar. O seletor de pasta é o único trecho que não cabe no ViewModel.</summary>
public partial class AbaConfigurar : UserControl
{
    public AbaConfigurar() => InitializeComponent();

    void AoEscolherPasta(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigBetaViewModel vm) return;
        var dialogo = new OpenFolderDialog { Title = "Pasta dos clones isolados", InitialDirectory = vm.PastaTrabalho };
        if (dialogo.ShowDialog(Window.GetWindow(this)) == true) vm.PastaTrabalho = dialogo.FolderName;
    }
}
