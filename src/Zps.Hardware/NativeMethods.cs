using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Zps.Hardware;

/// <summary>
/// Declaraciones P/Invoke crudas contra winspool.drv. Sin wrappers de terceros
/// (ni System.Drawing.Printing, ni System.Printing/WPF): acceso nativo directo
/// a la API Win32 del spooler de impresión, tal como usa cualquier driver RAW.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    public const uint PRINTER_ENUM_LOCAL = 0x00000002;
    public const uint PRINTER_ENUM_CONNECTIONS = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DOCINFOW
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pDocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string pDataType;
    }

    /// <summary>
    /// PRINTER_INFO_4: el nivel más liviano de EnumPrinters, pensado justamente para
    /// listar nombres de impresora rápido (equivalente a win32print.EnumPrinters del
    /// original en Python), sin abrir cada impresora individualmente.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PRINTER_INFO_4
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? pPrinterName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pServerName;
        public uint Attributes;
    }

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "OpenPrinterW")]
    public static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "StartDocPrinterW")]
    public static extern int StartDocPrinter(IntPtr hPrinter, int level, [In] ref DOCINFOW pDocInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "EnumPrintersW")]
    public static extern bool EnumPrinters(uint flags, string? name, uint level, IntPtr pPrinterEnum, uint cbBuf, out uint pcbNeeded, out uint pcReturned);
}
