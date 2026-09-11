using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Zps.Hardware;

/// <summary>
/// Envío RAW directo al spooler de Windows vía winspool.drv: OpenPrinter -> StartDocPrinter
/// (tipo de datos "RAW") -> StartPagePrinter -> WritePrinter -> EndPagePrinter -> EndDocPrinter
/// -> ClosePrinter. Puerto fiel y reforzado de enviar_raw (app_centralizada.py): aquí cada
/// paso que falla lanza RawPrintException con el código Win32 real en vez de devolver False
/// en silencio.
/// </summary>
[SupportedOSPlatform("windows")]
public static class RawPrinter
{
    public static void EnviarRaw(string nombreImpresora, string zpl, string nombreTrabajo = "Etiqueta ZPL")
    {
        ArgumentException.ThrowIfNullOrEmpty(nombreImpresora);
        ArgumentNullException.ThrowIfNull(zpl);

        var buffer = ZplBufferBuilder.Construir(zpl);

        if (!NativeMethods.OpenPrinter(nombreImpresora, out var hPrinter, IntPtr.Zero))
        {
            throw new RawPrintException($"No se pudo abrir la impresora '{nombreImpresora}'.", Marshal.GetLastWin32Error());
        }

        try
        {
            var docInfo = new NativeMethods.DOCINFOW
            {
                pDocName = nombreTrabajo,
                pOutputFile = null,
                pDataType = "RAW"
            };

            var idTrabajo = NativeMethods.StartDocPrinter(hPrinter, 1, ref docInfo);
            if (idTrabajo == 0)
            {
                throw new RawPrintException("No se pudo iniciar el trabajo de impresión (StartDocPrinter).", Marshal.GetLastWin32Error());
            }

            try
            {
                if (!NativeMethods.StartPagePrinter(hPrinter))
                {
                    throw new RawPrintException("No se pudo iniciar la página (StartPagePrinter).", Marshal.GetLastWin32Error());
                }

                try
                {
                    EscribirBuffer(hPrinter, buffer);
                }
                finally
                {
                    NativeMethods.EndPagePrinter(hPrinter);
                }
            }
            finally
            {
                NativeMethods.EndDocPrinter(hPrinter);
            }
        }
        finally
        {
            NativeMethods.ClosePrinter(hPrinter);
        }
    }

    private static void EscribirBuffer(IntPtr hPrinter, byte[] buffer)
    {
        var punteroNoAdministrado = Marshal.AllocHGlobal(buffer.Length);
        try
        {
            Marshal.Copy(buffer, 0, punteroNoAdministrado, buffer.Length);

            if (!NativeMethods.WritePrinter(hPrinter, punteroNoAdministrado, buffer.Length, out var bytesEscritos)
                || bytesEscritos != buffer.Length)
            {
                throw new RawPrintException(
                    $"Escritura incompleta al spooler: se escribieron {bytesEscritos} de {buffer.Length} bytes.",
                    Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(punteroNoAdministrado);
        }
    }
}
