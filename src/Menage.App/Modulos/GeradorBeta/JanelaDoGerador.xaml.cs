using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Menage.App.Controles;
using Microsoft.Win32;

namespace Menage.App.Modulos.GeradorBeta;

public static class ConversoresDoGerador
{
    public static IValueConverter VisivelSeNaoNulo { get; } = new NaoNuloConverter();

    sealed class NaoNuloConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is null ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

/// <summary>Só liga eventos da tela ao <see cref="GeradorBetaViewModel"/>; nenhuma regra de negócio mora aqui.</summary>
public partial class JanelaDoGerador : JanelaBase, IJanelaComParametro
{
    public const string Id = "beta.gerador";

    /// <summary>Parâmetro de abertura: abre direto o diálogo de entregas.</summary>
    public const string AbrirEntregas = "entregas";

    readonly GeradorBetaViewModel _vm;
    readonly DispatcherTimer _relogio = new() { Interval = TimeSpan.FromSeconds(1) };
    bool _iniciado;
    bool _abrirEntregas;

    public JanelaDoGerador(ContextoDeJanela contexto, GeradorBetaViewModel vm) : base(contexto)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        _relogio.Tick += (_, _) => _vm.Tique();
        ((INotifyCollectionChanged)vm.Log.Linhas).CollectionChanged += AoMudarLog;
        vm.PropertyChanged += AoMudarVm;
        Loaded += async (_, _) =>
        {
            _iniciado = true;
            await _vm.IniciarAsync();
            _relogio.Start();
            if (_abrirEntregas) await _vm.AbrirEntregasAsync();
        };
        Closing += AoFechar;
    }

    public void Receber(object? parametro)
    {
        _abrirEntregas = parametro as string == AbrirEntregas;
        if (_iniciado)
        {
            _vm.AplicarMarcadosDaJanelaPrincipal();
            if (_abrirEntregas) _ = _vm.AbrirEntregasAsync();
        }
    }

    void AoFechar(object? sender, CancelEventArgs e)
    {
        if (!_vm.Executando) { _relogio.Stop(); return; }
        e.Cancel = true;
        _vm.Mensagem = "Há uma geração em andamento: use Parar fila antes de fechar.";
    }

    void AoMudarLog(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_vm.Log.Acompanhar && e.Action == NotifyCollectionChangedAction.Add && ListaDoLog.Items.Count > 0)
            ListaDoLog.ScrollIntoView(ListaDoLog.Items[^1]);
    }

    void AoMudarVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GeradorBetaViewModel.SelecionadaNaTela) || _vm.SelecionadaNaTela is not { } linha) return;
        Grade.SelectedItem = linha;
        Grade.ScrollIntoView(linha);
    }

    // ---- modo, base, ferramentas

    void AoEscolherInstall(object sender, RoutedEventArgs e) => DefinirModo(false);

    void AoEscolherDeploy(object sender, RoutedEventArgs e) => DefinirModo(true);

    void DefinirModo(bool deploy)
    {
        if (_vm.Executando) { ChipInstall.SetCurrentValue(ToggleButton.IsCheckedProperty, !_vm.Deploy); ChipDeploy.SetCurrentValue(ToggleButton.IsCheckedProperty, _vm.Deploy); return; }
        _vm.Deploy = deploy;
        ChipInstall.SetCurrentValue(ToggleButton.IsCheckedProperty, !_vm.Deploy);
        ChipDeploy.SetCurrentValue(ToggleButton.IsCheckedProperty, _vm.Deploy);
    }

    async void AoAdicionarBase(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFolderDialog { Title = "Pasta base dos projetos (ex.: C:\\wmw\\dev\\dev1)" };
        if (dialogo.ShowDialog(this) == true) await _vm.AdicionarBaseAsync(dialogo.FolderName);
    }

    async void AoLimparTarget(object sender, RoutedEventArgs e) => await _vm.EscolherLimparTargetAsync();

    async void AoEntregas(object sender, RoutedEventArgs e) => await _vm.AbrirEntregasAsync();

    async void AoAtualizarBranches(object sender, RoutedEventArgs e) => await _vm.CarregarLinhasAsync();

    // ---- seleção

    void AoSelecionar(object sender, RoutedEventArgs e) => _vm.Selecionar((string)((FrameworkElement)sender).Tag);

    async void AoOrdem(object sender, RoutedEventArgs e) => await _vm.PedirOrdemAsync();

    // Um clique na linha marca/desmarca (Ctrl e Shift só selecionam); a coluna do comando não marca.
    void AoClicarNaGrade(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        if (e.OriginalSource is not DependencyObject origem) return;
        var celula = Acima<DataGridCell>(origem);
        if (celula is null || Acima<DataGridRow>(origem)?.Item is not LinhaDeBeta linha) return;
        if (celula.IsEditing) return;
        _vm.Alternar(linha);
    }

    void AoTeclarNaGrade(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || Keyboard.FocusedElement is TextBox) return;
        GeradorBetaViewModel.MarcarSelecionadas(Grade.SelectedItems.OfType<LinhaDeBeta>());
        e.Handled = true;
    }

    void AoMudarSelecao(object sender, SelectionChangedEventArgs e)
    {
        if (Grade.SelectedItem is LinhaDeBeta linha) _vm.Log.Mostrar(linha);
    }

    // Comando do projeto selecionado: confirma ao sair do campo ou com Enter.
    void AoComandoPerdeuFoco(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: LinhaDeBeta linha } campo || campo.Text == linha.Comando) return;
        var texto = campo.Text;
        Dispatcher.BeginInvoke(async () => await _vm.EditarComandoAsync(linha, texto), DispatcherPriority.Background);
    }

    void AoTeclarNoComando(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Grade.Focus();
        e.Handled = true;
    }

    void AoClicarNaFila(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not LinhaDeBeta linha) return;
        Grade.SelectedItem = linha;
        Grade.ScrollIntoView(linha);
    }

    static T? Acima<T>(DependencyObject origem) where T : DependencyObject
    {
        for (var atual = origem; atual is not null; atual = System.Windows.Media.VisualTreeHelper.GetParent(atual))
            if (atual is T alvo) return alvo;
        return null;
    }

    // ---- favoritos

    static string NomeDoMenu(object sender) => ((CartaoDeFavorito)((FrameworkElement)sender).DataContext).Nome;

    async void AoSalvarFavorito(object sender, RoutedEventArgs e) => await _vm.SalvarFavoritoAsync();

    async void AoAplicarFavorito(object sender, RoutedEventArgs e) => await _vm.AplicarFavoritoAsync(((CartaoDeFavorito)((FrameworkElement)sender).DataContext).Nome, gerar: false);

    async void AoMenuAplicar(object sender, RoutedEventArgs e) => await _vm.AplicarFavoritoAsync(NomeDoMenu(sender), gerar: false);

    async void AoMenuAplicarEGerar(object sender, RoutedEventArgs e) => await _vm.AplicarFavoritoAsync(NomeDoMenu(sender), gerar: true);

    async void AoMenuAtualizar(object sender, RoutedEventArgs e) => await _vm.AtualizarFavoritoAsync(NomeDoMenu(sender));

    async void AoMenuRenomear(object sender, RoutedEventArgs e) => await _vm.RenomearFavoritoAsync(NomeDoMenu(sender));

    async void AoMenuExcluir(object sender, RoutedEventArgs e) => await _vm.ExcluirFavoritoAsync(NomeDoMenu(sender));

    // ---- geração, log e entrega

    async void AoGerar(object sender, RoutedEventArgs e) => await _vm.GerarAsync();

    void AoParar(object sender, RoutedEventArgs e) => _vm.PararAposAtual();

    void AoInterromper(object sender, RoutedEventArgs e) => _vm.InterromperAgora();

    void AoAbrirLog(object sender, RoutedEventArgs e) => _vm.AbrirLog();

    void AoAbrirPastaDeLogs(object sender, RoutedEventArgs e) => _vm.AbrirPastaDeLogs();

    async void AoAbrirCmd(object sender, RoutedEventArgs e) => await _vm.AbrirCmdAsync(Grade.SelectedItem as LinhaDeBeta);

    void AoCopiarEntrega(object sender, RoutedEventArgs e) => _vm.CopiarEntregaDeNovo();

    void AoAbrirEntrega(object sender, RoutedEventArgs e) => _vm.AbrirEntrega();
}
