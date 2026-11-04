using System.Globalization;
using System.Windows.Data;

namespace Menage.App.Controles;

/// <summary>Conversores sem estado para XAML.</summary>
public static class Conversores
{
    /// <summary>Nome de ícone do catálogo ("ArrowSync") para o símbolo do WPF UI.</summary>
    public static IValueConverter IconeDoComando { get; } = new IconeConverter();

    sealed class IconeConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            FabricaDeComandos.Icone(value as string);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
