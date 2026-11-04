using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Menage.Pocs.Comum;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Menage.Pocs.WpfUi;

public record ItemMenu(string Texto, SymbolRegular Icone);

public partial class MainWindow : FluentWindow, IJanelaPoc
{
    private readonly CatalogoPaletas _paletas = new();
    private CancellationTokenSource? _cancelaDialogo;
    private bool _escuro;
    private Paleta _paleta;

    public MainWindow()
    {
        _paleta = _paletas.Todas[0];
        InitializeComponent();
        Menu.ItemsSource = new ItemMenu[]
        {
            new("Repositórios", SymbolRegular.Folder24), new("Branches", SymbolRegular.BranchFork24),
            new("Maven", SymbolRegular.Box24), new("Nexus", SymbolRegular.CloudArrowUp24), new("Configurações", SymbolRegular.Settings24)
        };
        Paletas.ItemsSource = _paletas.Todas;
        GradeDados.ItemsSource = new GeradorAmostra().Gerar();
        Animador = new AnimadorBarraLateral(HospedeiroBarra, Barra, Conteudo, ColunaBarra, 56, 220);
        AplicarTema(false, _paleta);
    }

    public Window Janela => this;
    public string Biblioteca => "WPF UI";
    public AnimadorBarraLateral Animador { get; }
    public System.Windows.Controls.DataGrid Grade => GradeDados;

    public void AplicarTema(bool escuro, Paleta paleta)
    {
        (_escuro, _paleta) = (escuro, paleta);
        var tema = escuro ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(tema, WindowBackdropType.None, false);
        ApplicationAccentColorManager.Apply(escuro ? paleta.Escuro : paleta.Claro, tema);
    }

    public void AbrirDialogo()
    {
        _cancelaDialogo = new CancellationTokenSource();
        var dialogo = new ContentDialog(HospedeiroDialogo)
        {
            Title = "Confirmar operação",
            Content = "Diálogo modal de teste: o fundo fica bloqueado até fechar.",
            PrimaryButtonText = "Confirmar",
            CloseButtonText = "Cancelar"
        };
        _ = dialogo.ShowAsync(_cancelaDialogo.Token);
    }

    public void FecharDialogo() => _cancelaDialogo?.Cancel();

    private void AoAlternarBarra(object s, RoutedEventArgs e) => Animador.Alternar();
    private void AoAlternarTema(object s, RoutedEventArgs e) => AplicarTema(!_escuro, _paleta);
    private void AoAbrirDialogo(object s, RoutedEventArgs e) => AbrirDialogo();
    private void AoEscolherPaleta(object s, RoutedEventArgs e) => AplicarTema(_escuro, (Paleta)((FrameworkElement)s).Tag);
}
