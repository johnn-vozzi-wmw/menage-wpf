using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Menage.App.Modulos.Personalizacao;

/// <summary>O ícone do tema escolhido (ou o "M" padrão) com o zoom aplicado e cortado nas bordas; usado no quadro e nas maquetes.</summary>
public partial class IconePrevia : UserControl
{
    public static readonly DependencyProperty EsticaProperty = DependencyProperty.Register(
        nameof(Estica), typeof(Stretch), typeof(IconePrevia), new PropertyMetadata(Stretch.Uniform));

    public IconePrevia() => InitializeComponent();

    /// <summary>Uniform mostra a imagem inteira; UniformToFill preenche o quadro cortando o excesso.</summary>
    public Stretch Estica { get => (Stretch)GetValue(EsticaProperty); set => SetValue(EsticaProperty, value); }
}
