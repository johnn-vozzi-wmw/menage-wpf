using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using Menage.Core.Comandos;
using Wpf.Ui.Controls;

namespace Menage.App.Controles;

/// <summary>Monta pílulas e menus a partir do catálogo: a mesma lista que a paleta Ctrl+K lê.</summary>
public static class FabricaDeComandos
{
    /// <summary>Nome do ícone no catálogo ("ArrowSync") para o símbolo do WPF UI, com ou sem o tamanho.</summary>
    public static SymbolRegular Icone(string? nome)
    {
        if (nome is null) return SymbolRegular.Apps24;
        return Enum.TryParse<SymbolRegular>(nome + "24", out var icone) || Enum.TryParse(nome, out icone) ? icone : SymbolRegular.Apps24;
    }

    public static Pilula Pilula(IComando comando) => new()
    {
        Texto = comando.Texto,
        Icone = Icone(comando.Icone),
        IsEnabled = comando.PodeExecutar(),
        Command = new AsyncRelayCommand(() => comando.ExecutarAsync()),
    };

    public static ContextMenu Menu(IEnumerable<IComando> comandos)
    {
        var menu = new ContextMenu();
        foreach (var comando in comandos)
        {
            menu.Items.Add(new Wpf.Ui.Controls.MenuItem
            {
                Header = comando.Texto,
                Icon = new SymbolIcon(Icone(comando.Icone)),
                InputGestureText = comando.Atalho ?? string.Empty,
                IsEnabled = comando.PodeExecutar(),
                Command = new AsyncRelayCommand(() => comando.ExecutarAsync()),
            });
        }

        return menu;
    }
}
