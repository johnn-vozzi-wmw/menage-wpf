using System.Globalization;
using System.Windows.Data;

namespace Menage.App.Modulos.Personalizacao;

/// <summary>RadioButton ligado a um enum: marcado quando o valor tem o nome do parâmetro; marcar grava esse valor.</summary>
public sealed class ConversorDeOpcao : IValueConverter
{
    public static ConversorDeOpcao Instancia { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && string.Equals(value.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string nome ? Enum.Parse(targetType, nome) : Binding.DoNothing;
}

/// <summary>Verdadeiro quando o valor não é nulo (habilita campos que dependem de uma seleção).</summary>
public sealed class ConversorDeNulo : IValueConverter
{
    public static ConversorDeNulo Instancia { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
