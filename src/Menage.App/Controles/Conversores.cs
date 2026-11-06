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

    /// <summary>Número igual a zero vira Visible (para "Nada por aqui" em listas vazias); qualquer outro, Collapsed.</summary>
    public static IValueConverter ZeroVisivel { get; } = new NumeroConverter(n => n == 0 ? Visibility.Visible : Visibility.Collapsed);

    public static IValueConverter Inverter { get; } = new BoolConverter(v => !v);

    sealed class IconeConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            FabricaDeComandos.Icone(value as string);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    sealed class NumeroConverter(Func<int, object> converter) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => converter(value is int n ? n : 0);

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
