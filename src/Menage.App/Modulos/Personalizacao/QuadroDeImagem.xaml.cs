using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Menage.App.Modulos.Personalizacao;

/// <summary>Só a mecânica de arrastar e soltar; quem valida e grava o arquivo é o <see cref="IconesViewModel"/>.</summary>
public partial class QuadroDeImagem : UserControl
{
    public QuadroDeImagem() => InitializeComponent();

    IconesViewModel? Modelo => DataContext as IconesViewModel;

    void AoClicarNaZona(object sender, MouseButtonEventArgs e) => Modelo?.EscolherImagemCommand.Execute(null);

    void AoArrastarSobre(object sender, DragEventArgs e)
    {
        var ok = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        Zona.Tag = ok ? "sobre" : "";
        e.Handled = true;
    }

    void AoSairDoArrasto(object sender, DragEventArgs e) => Zona.Tag = "";

    void AoSoltar(object sender, DragEventArgs e)
    {
        Zona.Tag = "";
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } arquivos && File.Exists(arquivos[0])) Modelo?.ReceberArquivo(arquivos[0]);
        e.Handled = true;
    }
}
