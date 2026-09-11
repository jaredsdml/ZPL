using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Zps.UI.Converters;

/// <summary>Null (o byte[] vacío) -> Collapsed; cualquier otro valor -> Visible. Con ConverterParameter="Invert" se invierte.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var esNuloOVacio = value is null || (value is byte[] bytes && bytes.Length == 0);
        var invertir = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        if (invertir)
        {
            esNuloOVacio = !esNuloOVacio;
        }

        return esNuloOVacio ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
