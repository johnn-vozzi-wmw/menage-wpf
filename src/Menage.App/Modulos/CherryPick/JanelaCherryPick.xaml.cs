using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Menage.App.Controles;
using Wpf.Ui.Controls;

namespace Menage.App.Modulos.CherryPick;

/// <summary>Histórico dos projetos marcados. Só liga controles ao ViewModel: nenhuma regra de negócio mora aqui.</summary>
public partial class JanelaCherryPick : JanelaBase
{
    readonly JanelaCherryPickViewModel _modelo;

    public JanelaCherryPick(ContextoDeJanela contexto, JanelaCherryPickViewModel modelo) : base(contexto)
    {
        _modelo = modelo;
        DataContext = modelo;
        InitializeComponent();
        _modelo.Aviso += (titulo, texto, grave) => Dispatcher.InvokeAsync(() => Avisar(titulo, texto, grave ? ControlAppearance.Danger : ControlAppearance.Success));
        _modelo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(JanelaCherryPickViewModel.AbasVisiveis)) Abas.Visibility = _modelo.AbasVisiveis ? Visibility.Visible : Visibility.Collapsed;
        };
        InputBindings.Add(new KeyBinding(new CommunityToolkit.Mvvm.Input.RelayCommand(() => CampoBusca.Focus()), Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new CommunityToolkit.Mvvm.Input.RelayCommand(AoAlternarLayoutComando), Key.L, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new CommunityToolkit.Mvvm.Input.RelayCommand(() => _ = AoCherryPickComando()), Key.Enter, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new CommunityToolkit.Mvvm.Input.RelayCommand(() => _ = _modelo.CarregarAsync()), Key.F5, ModifierKeys.None));
        Loaded += (_, _) => _ = _modelo.CarregarAsync();
        Closed += (_, _) => _modelo.Dispose();
    }

    public JanelaCherryPickViewModel Modelo => _modelo;

    void AoLimparFiltros(object sender, RoutedEventArgs e) => _modelo.LimparFiltros();

    void AoAlternarLayout(object sender, RoutedEventArgs e) => AoAlternarLayoutComando();

    void AoAlternarLayoutComando() => _modelo.AlternarLayout();

    void AoAbrirFila(object sender, RoutedEventArgs e) => _modelo.AbrirFila();

    async void AoAdicionarFila(object sender, RoutedEventArgs e) => await _modelo.AdicionarAFilaAsync();

    async void AoStash(object sender, RoutedEventArgs e) => await _modelo.GuardarEmStashAsync();

    async void AoCherryPick(object sender, RoutedEventArgs e) => await AoCherryPickComando();

    Task AoCherryPickComando() => _modelo.PodeAgir ? _modelo.AplicarAgoraAsync() : Task.CompletedTask;

    async void AoVerBranches(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PainelDeHistorico painel) await painel.VerBranchesAsync();
    }

    // Espaço marca ou desmarca todas as linhas selecionadas (Shift e Ctrl selecionam várias).
    void AoTeclaNaGrade(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || sender is not System.Windows.Controls.DataGrid { DataContext: PainelDeHistorico painel } grade) return;
        PainelDeHistorico.Alternar(grade.SelectedItems.OfType<LinhaDeCommit>());
        e.Handled = true;
    }

    void AoDuploClique(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.DataGrid { DataContext: PainelDeHistorico painel }) return;
        if (e.OriginalSource is DependencyObject origem && ProcurarAcima<DataGridRow>(origem)?.Item is LinhaDeCommit linha) PainelDeHistorico.Alternar([linha]);
    }

    static T? ProcurarAcima<T>(DependencyObject? filho) where T : DependencyObject
    {
        while (filho is not null and not T) filho = System.Windows.Media.VisualTreeHelper.GetParent(filho);
        return filho as T;
    }
}
