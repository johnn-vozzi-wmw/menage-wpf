using System.Windows;
using System.Windows.Controls;

namespace Menage.App.Modulos.BetaAutomatica;

/// <summary>Aba JQL. Os botões de trechos prontos (marcadores, AND, ORDER BY...) inserem no ponto do cursor do editor.</summary>
public partial class AbaJql : UserControl
{
    public AbaJql()
    {
        InitializeComponent();
        foreach (var (texto, trecho) in JqlViewModel.TrechosProntos)
        {
            var botao = new Wpf.Ui.Controls.Button { Content = texto, Margin = new Thickness(0, 0, 6, 4), Tag = trecho };
            botao.Click += AoInserir;
            Trechos.Items.Add(botao);
        }
    }

    void AoInserir(object sender, RoutedEventArgs e)
    {
        if (DataContext is not JqlViewModel vm || ((FrameworkElement)sender).Tag is not string trecho) return;
        var cursor = vm.Inserir(trecho, Editor.SelectionStart, Editor.SelectionLength);
        Editor.Focus();
        Editor.SelectionStart = cursor;
        Editor.SelectionLength = 0;
    }
}
