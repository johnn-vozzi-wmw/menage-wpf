using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;

namespace Menage.App.Modulos.BetaAutomatica;

/// <summary>Console ancorado: filtros e log. Copiar, salvar, abrir o arquivo e arrastar a alça são ações do Windows, então ficam aqui.</summary>
public partial class DockDoConsole : UserControl
{
    ConsoleDoDockViewModel? _vm;

    public DockDoConsole()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null) _vm.RolarParaOFim -= Rolar;
            _vm = e.NewValue as ConsoleDoDockViewModel;
            if (_vm is not null) _vm.RolarParaOFim += Rolar;
        };
    }

    /// <summary>Ctrl+F: expande o console e leva o cursor à busca.</summary>
    public void FocarBusca()
    {
        _vm?.ExpandirDock();
        Dispatcher.BeginInvoke(() => { Busca.Focus(); Busca.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    void Rolar() => Dispatcher.BeginInvoke(() =>
    {
        if (Linhas.Items.Count > 0) Linhas.ScrollIntoView(Linhas.Items[^1]);
    }, System.Windows.Threading.DispatcherPriority.Background);

    void AoArrastar(object sender, DragDeltaEventArgs e) => _vm?.Redimensionar(e.VerticalChange);

    void AoAbrirMais(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: { } menu } alvo) return;
        menu.PlacementTarget = alvo;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }

    void AoCopiar(object sender, RoutedEventArgs e)
    {
        if (_vm is { Visiveis.Count: > 0 }) Clipboard.SetText(_vm.TextoNaTela());
    }

    void AoSalvar(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var d = new SaveFileDialog { FileName = $"console-beta-{DateTime.Now:yyyyMMdd-HHmmss}.txt", Filter = "Texto|*.txt" };
        if (d.ShowDialog(Window.GetWindow(this)) == true) File.WriteAllText(d.FileName, _vm.TextoNaTela());
    }

    void AoAbrirLog(object sender, RoutedEventArgs e)
    {
        if (_vm?.ArquivoDoLog is { Length: > 0 } a && File.Exists(a)) Process.Start(new ProcessStartInfo(a) { UseShellExecute = true });
        else (Window.GetWindow(this) as Controles.JanelaBase)?.Avisar("Console", "Ainda não há console-beta.log.");
    }
}
