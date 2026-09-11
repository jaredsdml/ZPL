namespace Zps.Hardware;

/// <summary>
/// Error concreto de una llamada al spooler de Windows (winspool.drv), con el código
/// Win32 crudo adjunto para diagnóstico. A diferencia del original en Python
/// (enviar_raw devolvía True/False silenciosamente), aquí el fallo es explícito.
/// </summary>
public sealed class RawPrintException : Exception
{
    public int CodigoErrorWin32 { get; }

    public RawPrintException(string mensaje, int codigoErrorWin32)
        : base($"{mensaje} (código de error Win32: {codigoErrorWin32})")
    {
        CodigoErrorWin32 = codigoErrorWin32;
    }
}
