using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Menage.App.Controles;

/// <summary>Conversores sem estado para XAML.</summary>
public static class Conversores
{
    /// <summary>Nome de ícone do catálogo ("ArrowSync") para o símbolo do WPF UI.</summary>
    public static IValueConverter IconeDoComando { get; } = new IconeConverter();

    /// <summary>true vira Visible; false vira Collapsed.</summary>
    public static IValueConverter VisivelSe { get; } = new BoolConverter(v => v ? Visibility.Visible : Visibility.Collapsed);

    /// <summary>true vira Collapsed; false vira Visible.</summary>
    public static IValueConverter OcultoSe { get; } = new BoolConverter(v => v ? Visibility.Collapsed : Visibility.Visible);

    public static IValueConverter Inverter { get; } = new BoolConverter(v => !v);

    sealed class IconeConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            FabricaDeComandos.Icone(value as string);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    sealed class BoolConverter(Func<bool, object> converter) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => converter(value is true);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
