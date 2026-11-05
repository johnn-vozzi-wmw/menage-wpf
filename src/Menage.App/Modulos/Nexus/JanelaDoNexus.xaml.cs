using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Menage.App.Controles;
using Menage.Core.Nexus;
using Microsoft.Win32;

namespace Menage.App.Modulos.Nexus;

/// <summary>Só liga eventos da tela ao <see cref="NexusViewModel"/>; nenhuma regra de negócio mora aqui.</summary>
public partial class JanelaDoNexus : JanelaBase
{
    public const string Id = "nexus.janela";

    readonly NexusViewModel _vm;

    public JanelaDoNexus(ContextoDeJanela contexto, NexusViewModel vm) : base(contexto)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        InputBindings.Add(new KeyBinding(vm.BuscarCommand, Key.F5, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(vm.BaixarCommand, Key.Enter, ModifierKeys.Control));
        Loaded += async (_, _) =>
        {
            await _vm.IniciarAsync();
            CampoNames.Focus();
        };
    }

    void AoAlternarServidor(object sender, RoutedEventArgs e) => _vm.ServidorAberto = !_vm.ServidorAberto;

    void AoMudarSenha(object sender, RoutedEventArgs e) => _vm.SenhaDigitada = Senha.Password;

    void AoTeclarFiltro(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (_vm.BuscarCommand.CanExecute(null)) _vm.BuscarCommand.Execute(null);
    }

    void AoEscolherPasta(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFolderDialog { Title = "Pasta onde salvar os arquivos do Nexus" };
        if (dialogo.ShowDialog(this) == true) _vm.Destino = dialogo.FolderName;
    }

    void AoAplicarFavorito(object sender, RoutedEventArgs e) => _vm.AplicarFavoritoCommand.Execute(((FrameworkElement)sender).Tag as FavoritoNexus);

    void AoExcluirFavorito(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)((MenuItem)sender).Parent;
        _vm.ExcluirFavoritoCommand.Execute((menu.PlacementTarget as FrameworkElement)?.Tag as FavoritoNexus);
    }

    void AoMarcarTodos(object sender, RoutedEventArgs e) => _vm.MarcarTodosCommand.Execute(true);

    void AoDesmarcar(object sender, RoutedEventArgs e) => _vm.MarcarTodosCommand.Execute(false);

    void AoClicarNaGrade(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        if (e.OriginalSource is DependencyObject origem && Acima<DataGridCell>(origem) is not null && Acima<DataGridRow>(origem)?.Item is LinhaDeComponente linha)
            linha.Marcado = !linha.Marcado;
    }

    void AoTeclarNaGrade(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        var linhas = Grade.SelectedItems.OfType<LinhaDeComponente>().ToList();
        var marcar = linhas.Any(l => !l.Marcado);
        foreach (var l in linhas) l.Marcado = marcar;
        e.Handled = true;
    }

    static T? Acima<T>(DependencyObject origem) where T : DependencyObject
    {
        for (var atual = origem; atual is not null; atual = VisualTreeHelper.GetParent(atual))
            if (atual is T alvo) return alvo;
        return null;
    }
}
