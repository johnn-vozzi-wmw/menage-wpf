using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Menage.Pocs.Comum;

namespace Menage.Pocs.Material;

public record ItemMenu(string Texto, PackIconKind Icone);

public partial class MainWindow : Window, IJanelaPoc
{
    private readonly CatalogoPaletas _paletas = new();
    private readonly PaletteHelper _helper = new();
    private bool _escuro;
    private Paleta _paleta;

    public MainWindow()
    {
        _paleta = _paletas.Todas[0];
        InitializeComponent();
        new ConfiguradorJanela().Aplicar(this, Raiz);
        Menu.ItemsSource = new ItemMenu[]
        {
            new("Repositórios", PackIconKind.FolderOutline), new("Branches", PackIconKind.SourceBranch),
            new("Maven", PackIconKind.PackageVariantClosed), new("Nexus", PackIconKind.CloudUploadOutline), new("Configurações", PackIconKind.CogOutline)
        };
        Paletas.ItemsSource = _paletas.Todas;
        GradeDados.ItemsSource = new GeradorAmostra().Gerar();
        Animador = new AnimadorBarraLateral(HospedeiroBarra, Barra, Conteudo, ColunaBarra, 56, 220);
        AplicarTema(false, _paleta);
    }

    public Window Janela => this;
    public string Biblioteca => "Material Design";
    public AnimadorBarraLateral Animador { get; }
    public DataGrid Grade => GradeDados;

    public void AplicarTema(bool escuro, Paleta paleta)
    {
        (_escuro, _paleta) = (escuro, paleta);
        var tema = _helper.GetTheme();
        tema.SetBaseTheme(escuro ? BaseTheme.Dark : BaseTheme.Light);
        var cor = escuro ? paleta.Escuro : paleta.Claro;
        tema.SetPrimaryColor(cor);
        tema.SetSecondaryColor(cor);
        _helper.SetTheme(tema);
    }

    public void AbrirDialogo() => _ = DialogHost.Show(new StackPanel
    {
        Margin = new Thickness(24),
        Children =
        {
            new TextBlock { Text = "Confirmar operação", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) },
            new TextBlock { Text = "Diálogo modal de teste: o fundo fica bloqueado até fechar.", Margin = new Thickness(0, 0, 0, 16) },
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children =
                {
                    new Button { Content = "Cancelar", Style = (Style)FindResource("MaterialDesignFlatButton"), Command = DialogHost.CloseDialogCommand, CommandParameter = false },
                    new Button { Content = "Confirmar", Command = DialogHost.CloseDialogCommand, CommandParameter = true }
                }
            }
        }
    }, "Raiz");

    public void FecharDialogo() => DialogHost.Close("Raiz");

    private void AoAlternarBarra(object s, RoutedEventArgs e) => Animador.Alternar();
    private void AoAlternarTema(object s, RoutedEventArgs e) => AplicarTema(!_escuro, _paleta);
    private void AoAbrirDialogo(object s, RoutedEventArgs e) => AbrirDialogo();
    private void AoEscolherPaleta(object s, RoutedEventArgs e) => AplicarTema(_escuro, (Paleta)((FrameworkElement)s).Tag);
}
