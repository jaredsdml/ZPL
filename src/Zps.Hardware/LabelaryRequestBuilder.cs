using System.Text;

namespace Zps.Hardware;

/// <summary>
/// Construcción pura (sin red) de la URL y el contenido multipart que espera la API de
/// Labelary, aislada para poder probarse sin hacer ninguna llamada HTTP real. Espejo de
/// la llamada a requests.post(url, files={'file': zpl}, ...) del original en Python.
/// </summary>
public static class LabelaryRequestBuilder
{
    private const string BaseUrl = "https://api.labelary.com/v1/printers/";

    public static string ConstruirUrl(string densidad, string tamanoEtiqueta, int rotacion)
    {
        ArgumentException.ThrowIfNullOrEmpty(densidad);
        ArgumentException.ThrowIfNullOrEmpty(tamanoEtiqueta);
        return $"{BaseUrl}{densidad}/labels/{tamanoEtiqueta}/{rotacion}/";
    }

    public static MultipartFormDataContent ConstruirContenido(string zpl)
    {
        ArgumentNullException.ThrowIfNull(zpl);
        return new MultipartFormDataContent
        {
            { new StringContent(zpl, Encoding.UTF8), "file" }
        };
    }
}
