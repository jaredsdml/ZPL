using System.Text;

namespace Zps.Hardware;

/// <summary>
/// Construye el buffer de bytes crudo que se envía al spooler como datos "RAW".
/// Función pura y aislada (sin P/Invoke) para que la composición del buffer sea
/// verificable con pruebas unitarias comunes. UTF-8, espejo de zpl.encode('utf-8')
/// en el enviar_raw original (necesario porque las plantillas activan ^CI28, la
/// página de códigos UTF-8 de Zebra, para imprimir acentos/ñ correctamente).
/// </summary>
public static class ZplBufferBuilder
{
    public static byte[] Construir(string zpl)
    {
        ArgumentNullException.ThrowIfNull(zpl);
        return Encoding.UTF8.GetBytes(zpl);
    }
}
