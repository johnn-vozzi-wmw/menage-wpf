using System.Windows;
using System.Windows.Controls;

namespace Menage.App.Controles;

/// <summary>Selo, título, texto, sugestão e detalhes. O que vier nulo ou vazio some.</summary>
public partial class ConteudoDeMensagem : UserControl
{
    public ConteudoDeMensagem(TipoDeSelo tipo, string titulo, string? texto = null, string? sugestao = null, IReadOnlyList<string>? detalhes = null)
    {
        InitializeComponent();
        SeloDaMensagem.Tipo = tipo;
        Titulo.Text = titulo;
        Preencher(Texto, texto);
        Preencher(Sugestao, sugestao);
        Detalhes.ItemsSource = detalhes;
        Detalhes.Visibility = detalhes is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    }

    static void Preencher(TextBlock bloco, string? valor)
    {
        bloco.Text = valor;
        bloco.Visibility = string.IsNullOrWhiteSpace(valor) ? Visibility.Collapsed : Visibility.Visible;
    }
}
