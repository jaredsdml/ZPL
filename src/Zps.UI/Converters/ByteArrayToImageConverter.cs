using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Zps.UI.Converters;

/// <summary>Convierte los bytes PNG devueltos por LabelaryPreviewService en un BitmapImage para el panel de vista previa.</summary>
public sealed class ByteArrayToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes)
        {
            return null;
        }

        var imagen = new BitmapImage();
        using var stream = new MemoryStream(bytes);
        imagen.BeginInit();
        imagen.CacheOption = BitmapCacheOption.OnLoad;
        imagen.StreamSource = stream;
        imagen.EndInit();
        imagen.Freeze();
        return imagen;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
