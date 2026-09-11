using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Zps.Hardware;

/// <summary>
/// Descubrimiento de impresoras instaladas vía Win32 EnumPrinters (nivel 4: el más liviano,
/// pensado solo para listar nombres). Espejo directo de obtener_impresoras_sistema()
/// (win32print.EnumPrinters) en el original, ahora sin la dependencia de pywin32.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PrinterDiscovery
{
    private const uint Flags = NativeMethods.PRINTER_ENUM_LOCAL | NativeMethods.PRINTER_ENUM_CONNECTIONS;
    private const uint Level = 4;

    public static IReadOnlyList<string> ListarInstaladas()
    {
        NativeMethods.EnumPrinters(Flags, null, Level, IntPtr.Zero, 0, out var bytesNecesarios, out _);
        if (bytesNecesarios == 0)
        {
            return Array.Empty<string>();
        }

        var buffer = Marshal.AllocHGlobal((int)bytesNecesarios);
        try
        {
            if (!NativeMethods.EnumPrinters(Flags, null, Level, buffer, bytesNecesarios, out _, out var cantidad))
            {
                throw new RawPrintException("No se pudo enumerar las impresoras instaladas (EnumPrinters).", Marshal.GetLastWin32Error());
            }

            var nombres = new List<string>((int)cantidad);
            var tamanoStruct = Marshal.SizeOf<NativeMethods.PRINTER_INFO_4>();

            for (var i = 0; i < cantidad; i++)
            {
                var punteroInfo = IntPtr.Add(buffer, i * tamanoStruct);
                var info = Marshal.PtrToStructure<NativeMethods.PRINTER_INFO_4>(punteroInfo);
                if (!string.IsNullOrEmpty(info.pPrinterName))
                {
                    nombres.Add(info.pPrinterName!);
                }
            }

            return nombres;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
