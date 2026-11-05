using Wpf.Ui.Controls;

namespace Menage.App.Controles;

/// <summary>
/// O TextBox do WPF UI com lupa e botão de limpar. O ícone nasce aqui, e não num Setter de estilo, porque
/// um elemento visual só pode estar num lugar: no estilo, o segundo campo aberto roubaria a lupa do primeiro.
/// </summary>
public sealed class CampoDeBusca : TextBox
{
    public CampoDeBusca()
    {
        Icon = new SymbolIcon(SymbolRegular.Search20);
        IconPlacement = ElementPlacement.Left;
        ClearButtonEnabled = true;
        PlaceholderText = "Buscar";
        System.Windows.Automation.AutomationProperties.SetName(this, "Buscar");
    }
}
