using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Menage.App.Controles;

/// <summary>
/// DataGrid padrão (estilo em Superficies.xaml): virtualizada, com hover e estado vazio. O estado vazio entra
/// por cima da área de linhas sem copiar o template do WPF UI: o cabeçalho continua visível.
/// </summary>
public sealed class GradePadrao : DataGrid
{
    public static readonly DependencyProperty TituloVazioProperty =
        DependencyProperty.Register(nameof(TituloVazio), typeof(string), typeof(GradePadrao), new PropertyMetadata("Nenhum item"));

    public static readonly DependencyProperty DescricaoVaziaProperty =
        DependencyProperty.Register(nameof(DescricaoVazia), typeof(string), typeof(GradePadrao));

    public string TituloVazio
    {
        get => (string)GetValue(TituloVazioProperty);
        set => SetValue(TituloVazioProperty, value);
    }

    public string? DescricaoVazia
    {
        get => (string?)GetValue(DescricaoVaziaProperty);
        set => SetValue(DescricaoVaziaProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("border") is not Border { Child: { } conteudo } borda) return;

        var vazio = new EstadoVazio { Margin = new Thickness(0, 36, 0, 0), IsHitTestVisible = false };
        vazio.SetBinding(EstadoVazio.TituloProperty, new Binding(nameof(TituloVazio)) { Source = this });
        vazio.SetBinding(EstadoVazio.DescricaoProperty, new Binding(nameof(DescricaoVazia)) { Source = this });
        vazio.SetBinding(VisibilityProperty, new Binding(nameof(HasItems)) { Source = this, Converter = new SemItensParaVisivel() });

        borda.Child = null;
        var area = new Grid();
        area.Children.Add(conteudo);
        area.Children.Add(vazio);
        borda.Child = area;
    }

    sealed class SemItensParaVisivel : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
