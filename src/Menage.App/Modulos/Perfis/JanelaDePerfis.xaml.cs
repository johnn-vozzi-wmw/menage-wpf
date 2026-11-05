using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Menage.App.Controles;
using Menage.Core.Perfis;
using Microsoft.Win32;

namespace Menage.App.Modulos.Perfis;

/// <summary>Mostra a base do perfil com nome por extenso.</summary>
public sealed class ConversorDeBase : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        PerfilDeUso.Desenvolvedor => "Desenvolvedor",
        PerfilDeUso.Tester => "Tester",
        _ => "Geral",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Só liga eventos da tela ao <see cref="PerfisViewModel"/> (seleção, caixas de arquivo).</summary>
public partial class JanelaDePerfis : JanelaBase
{
    public const string Id = "perfis.gerenciar";

    readonly PerfisViewModel _vm;
    bool _revertendo;

    public JanelaDePerfis(ContextoDeJanela contexto, PerfisViewModel vm) : base(contexto)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        var visao = CollectionViewSource.GetDefaultView(vm.Opcoes);
        visao.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ItemDeFuncionalidade.Grupo)));
        Funcionalidades.ItemsSource = visao;
    }

    async void AoSelecionar(object sender, SelectionChangedEventArgs e)
    {
        if (_revertendo || Lista.SelectedItem is not ItemDePerfil destino) return;
        if (await _vm.TrocarAsync(destino)) return;
        _revertendo = true;
        Lista.SelectedItem = _vm.Selecionado;
        _revertendo = false;
    }

    void AoImportar(object sender, RoutedEventArgs e)
    {
        var caixa = new OpenFileDialog { Title = "Importar perfis", Filter = "Perfis do Menage (*.json)|*.json|Todos os arquivos|*.*" };
        if (caixa.ShowDialog(this) == true) _vm.Importar(caixa.FileName);
    }

    void AoExportar(object sender, RoutedEventArgs e)
    {
        var caixa = new SaveFileDialog { Title = "Exportar perfis", Filter = "Perfis do Menage (*.json)|*.json", FileName = "perfis-menage.json" };
        if (caixa.ShowDialog(this) == true) _vm.Exportar(caixa.FileName);
    }
}
