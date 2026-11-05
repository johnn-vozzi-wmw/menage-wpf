using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Menage.App.Controles;
using Menage.App.Tema;
using Microsoft.Win32;

namespace Menage.App.Modulos.Configuracoes;

/// <summary>Só liga eventos da tela (troca de seção, caixas de arquivo, PasswordBox) ao <see cref="ConfiguracoesViewModel"/>.</summary>
public partial class JanelaDeConfiguracoes : JanelaBase, IJanelaComParametro
{
    public const string Id = "config.janela";

    /// <summary>Parâmetro de abertura que vai direto para a seção de importação do Git Manager.</summary>
    public const string AbrirImportacao = "importar";

    readonly ConfiguracoesViewModel _vm;
    readonly ScrollViewer[] _paineis;
    int _atual = -1;

    public JanelaDeConfiguracoes(ContextoDeJanela contexto, ConfiguracoesViewModel vm) : base(contexto)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        _paineis = [Painel0, Painel1, Painel2, Painel3, Painel4, Painel5, Painel6];
        foreach (var painel in _paineis) painel.RenderTransform = new TranslateTransform();
        Navegacao.ItemsSource = ConfiguracoesViewModel.Secoes;
        Navegacao.SelectedIndex = 0;
    }

    public void Receber(object? parametro)
    {
        if (parametro as string == AbrirImportacao) Navegacao.SelectedIndex = ConfiguracoesViewModel.Secoes.Count - 1;
    }

    void AoMudarSecao(object sender, SelectionChangedEventArgs e) => Mostrar(Navegacao.SelectedIndex);

    // Troca por tempo (ease-out) só em opacidade e deslocamento: nada de layout por quadro.
    void Mostrar(int indice)
    {
        if (indice < 0 || indice == _atual) return;
        var animar = _atual >= 0 && IsLoaded;
        if (_atual >= 0) _paineis[_atual].Visibility = Visibility.Collapsed;
        _atual = indice;
        var painel = _paineis[indice];
        painel.Visibility = Visibility.Visible;
        if (!animar) return;
        painel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Animacao.Media) { EasingFunction = Animacao.Entrada });
        ((TranslateTransform)painel.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, Animacao.Media) { EasingFunction = Animacao.Entrada });
    }

    async void AoEscolherRaiz(object sender, RoutedEventArgs e)
    {
        var caixa = new OpenFolderDialog { Title = "Pasta que contém web, framework e pda", InitialDirectory = _vm.Raiz };
        if (caixa.ShowDialog(this) == true) await _vm.DefinirRaizAsync(caixa.FolderName);
    }

    void AoEscolherChave(object sender, RoutedEventArgs e)
    {
        var caixa = new OpenFileDialog { Title = "Chave SSH (a .ppk ou a chave OpenSSH)", CheckFileExists = true };
        if (caixa.ShowDialog(this) != true) return;
        _vm.ChaveSsh = caixa.FileName.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase) ? caixa.FileName[..^4] : caixa.FileName;
    }

    void AoEscolherWinMerge(object sender, RoutedEventArgs e)
    {
        var caixa = new OpenFileDialog { Title = "WinMergeU.exe", Filter = "WinMerge (WinMergeU.exe)|WinMergeU.exe|Executáveis (*.exe)|*.exe" };
        if (caixa.ShowDialog(this) == true) _vm.WinMerge = caixa.FileName;
    }

    void AoEscolherTortoise(object sender, RoutedEventArgs e)
    {
        var caixa = new OpenFolderDialog { Title = "Pasta bin do TortoiseGit" };
        if (caixa.ShowDialog(this) == true) _vm.TortoiseGit = caixa.FolderName;
    }

    void AoEscolherPastaAntiga(object sender, RoutedEventArgs e)
    {
        var caixa = new OpenFolderDialog { Title = "Pasta do Git Manager antigo (a mesma do .exe)" };
        if (caixa.ShowDialog(this) == true) _vm.PastaAntiga = caixa.FolderName;
    }

    void AoEscolherInstalacao(object sender, SelectionChangedEventArgs e)
    {
        if (((ListBox)sender).SelectedItem is string pasta) _vm.PastaAntiga = pasta;
    }

    // O segredo sai do PasswordBox direto para o cofre e o campo é limpo: o VM nunca guarda o texto.
    void AoGuardarCredencial(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not PasswordBox caixa || ((FrameworkElement)sender).DataContext is not ItemDeCredencial item) return;
        _vm.GuardarCredencial(item, caixa.Password);
        caixa.Clear();
    }
}
