using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Menage.App.Controles;
using Microsoft.Win32;

namespace Menage.App.Modulos.Melhorias;

/// <summary>Dashboard das Melhorias. Só liga eventos ao <see cref="MelhoriasViewModel"/>; Esc limpa a busca.</summary>
public partial class JanelaMelhorias : JanelaBase
{
    public const string Id = "melhorias.painel";

    readonly MelhoriasViewModel _vm;

    public JanelaMelhorias(ContextoDeJanela contexto, MelhoriasViewModel vm) : base(contexto)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        Loaded += (_, _) => _vm.Recarregar();
        Busca.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || _vm.Consulta.Length == 0) return;
            _vm.LimparBusca();
            e.Handled = true;
        };
    }

    void AoClicarTag(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag) _vm.AlternarTermo("tag", tag);
    }

    static string? TagDoMenu(object sender) => ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget is FrameworkElement { Tag: string t } ? t : null;

    async void AoRenomearTag(object sender, RoutedEventArgs e)
    {
        if (TagDoMenu(sender) is { } tag) await _vm.RenomearTagAsync(tag);
    }

    async void AoRemoverTag(object sender, RoutedEventArgs e)
    {
        if (TagDoMenu(sender) is { } tag) await _vm.RemoverTagAsync(tag);
    }

    void AoCorDaTag(object sender, RoutedEventArgs e)
    {
        if (TagDoMenu(sender) is { } tag && sender is MenuItem { Tag: string cor }) _vm.DefinirCorDaTag(tag, cor.Length == 0 ? null : cor);
    }

    void AoClicarVisao(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is VisaoNaLista v) _vm.AplicarVisao(v.Consulta);
    }

    void AoRemoverVisao(object sender, RoutedEventArgs e)
    {
        if (((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget is FrameworkElement { Tag: VisaoNaLista { Salva: true } v }) _vm.RemoverVisao(v.Nome);
    }

    async void AoSalvarVisao(object sender, RoutedEventArgs e) => await _vm.SalvarVisaoAsync();

    async void AoNova(object sender, RoutedEventArgs e) => await _vm.NovaAsync();

    async void AoEditar(object sender, RoutedEventArgs e) => await _vm.EditarAsync(_vm.Selecionada?.Melhoria);

    async void AoAtivar(object sender, RoutedEventArgs e) => await _vm.AtivarAsync();

    async void AoDesvincular(object sender, RoutedEventArgs e) => await _vm.DesvincularAsync();

    async void AoExcluir(object sender, RoutedEventArgs e) => await _vm.ExcluirAsync();

    async void AoDuploClique(object sender, MouseButtonEventArgs e)
    {
        if (_vm.PodeAtivar && e.OriginalSource is DependencyObject origem && ItemsControl.ContainerFromElement(Lista, origem) is not null) await _vm.AtivarAsync();
    }

    void AoVersaoConf(object sender, RoutedEventArgs e) => _vm.LigarVersaoConf(((CheckBox)sender).IsChecked == true);

    void AoSqlite(object sender, RoutedEventArgs e)
    {
        var ligar = ((CheckBox)sender).IsChecked == true;
        if (_vm.LigarSqlite(ligar)) return;
        if (EscolherArquivo()) _vm.LigarSqlite(true);
        else CaixaSqlite.IsChecked = false;
    }

    void AoEscolherSqlite(object sender, RoutedEventArgs e) => EscolherArquivo();

    bool EscolherArquivo()
    {
        var d = new OpenFileDialog { Title = "Escolha o wmwvendas.sqlite3 (caminho completo)", Filter = "sqlite|*.sqlite3;*.sqlite;*.db|Todos|*.*" };
        if (d.ShowDialog(this) != true) return false;
        _vm.DefinirCaminhoDoSqlite(d.FileName);
        return true;
    }

    async void AoAplicarPerfil(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string id) await _vm.AplicarPerfilAgoraAsync(id);
    }

    void AoUsarAoAtivar(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string id) _vm.DefinirPerfilAoAtivar(id);
    }
}
