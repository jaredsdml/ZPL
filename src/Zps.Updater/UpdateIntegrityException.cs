namespace Zps.Updater;

/// <summary>Se lanza cuando el checksum SHA-256 publicado junto al asset no coincide con el archivo descargado.</summary>
public sealed class UpdateIntegrityException : Exception
{
    public UpdateIntegrityException(string mensaje) : base(mensaje)
    {
    }
}
