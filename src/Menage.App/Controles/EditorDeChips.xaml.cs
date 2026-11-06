using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Menage.App.Controles;

/// <summary>Lista de valores como chips removíveis, com um campo no fim: Enter, vírgula ou sair do campo adiciona; Backspace no campo vazio remove o último.</summary>
public partial class EditorDeChips : UserControl
{
    public static readonly DependencyProperty ItensProperty =
        DependencyProperty.Register(nameof(Itens), typeof(ObservableCollection<string>), typeof(EditorDeChips));

    public static readonly DependencyProperty DicaProperty =
        DependencyProperty.Register(nameof(Dica), typeof(string), typeof(EditorDeChips), new PropertyMetadata("", (d, e) => ((EditorDeChips)d).Entrada.ToolTip = e.NewValue));

    public EditorDeChips() => InitializeComponent();

    public ObservableCollection<string>? Itens
    {
        get => (ObservableCollection<string>?)GetValue(ItensProperty);
        set => SetValue(ItensProperty, value);
    }

    /// <summary>Texto de ajuda do campo (aparece como dica ao passar o mouse).</summary>
    public string Dica
    {
        get => (string)GetValue(DicaProperty);
        set => SetValue(DicaProperty, value);
    }

    void AoRemover(object sender, RoutedEventArgs e)
    {
        if (sender is Chip { Texto: var texto }) Itens?.Remove(texto);
    }

    void AoTecla(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.OemComma)
        {
            e.Handled = true;
            Adicionar();
        }
        else if (e.Key == Key.Back && Entrada.Text.Length == 0 && Itens is { Count: > 0 } itens) itens.RemoveAt(itens.Count - 1);
    }

    void AoPerderFoco(object sender, RoutedEventArgs e) => Adicionar();

    void Adicionar()
    {
        var texto = Entrada.Text.Trim().Trim(',', ';').Trim();
        Entrada.Clear();
        if (texto.Length == 0 || Itens is null || Itens.Any(i => i.Equals(texto, StringComparison.OrdinalIgnoreCase))) return;
        Itens.Add(texto);
    }
}
