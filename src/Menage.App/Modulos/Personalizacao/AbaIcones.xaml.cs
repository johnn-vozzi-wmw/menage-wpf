using System.Windows;
using System.Windows.Controls;

namespace Menage.App.Modulos.Personalizacao;

/// <summary>Aba Ícones: três cartões lado a lado e, abaixo de 980 px, empilhados. Só reorganiza a grade; nenhuma regra mora aqui.</summary>
public partial class AbaIcones : UserControl
{
    const double LarguraDeUmaColuna = 980;

    bool? _umaColuna;

    public AbaIcones() => InitializeComponent();

    void AoMudarTamanho(object sender, SizeChangedEventArgs e)
    {
        var estreita = e.NewSize.Width < LarguraDeUmaColuna;
        if (_umaColuna == estreita) return;
        _umaColuna = estreita;
        Col1.Width = estreita ? new GridLength(1, GridUnitType.Star) : new GridLength(230);
        Col2.Width = estreita ? new GridLength(0) : new GridLength(1.2, GridUnitType.Star);
        Col3.Width = estreita ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Posicionar(Tema, 0, 0);
        Posicionar(Imagem, estreita ? 1 : 0, estreita ? 0 : 1);
        Posicionar(Onde, estreita ? 2 : 0, estreita ? 0 : 2);
        Tema.Margin = estreita ? new Thickness(0, 0, 0, 10) : new Thickness(0, 0, 7, 0);
        Imagem.Margin = estreita ? new Thickness(0, 0, 0, 10) : new Thickness(7, 0, 7, 0);
        Onde.Margin = estreita ? new Thickness(0) : new Thickness(7, 0, 0, 0);
    }

    static void Posicionar(FrameworkElement elemento, int linha, int coluna)
    {
        Grid.SetRow(elemento, linha);
        Grid.SetColumn(elemento, coluna);
    }
}
