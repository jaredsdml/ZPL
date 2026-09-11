namespace Zps.Hardware;

public sealed record ResultadoPreview(bool Exito, byte[]? ImagenPng, string? Error)
{
    public static ResultadoPreview Ok(byte[] imagenPng) => new(true, imagenPng, null);

    public static ResultadoPreview Fallo(string error) => new(false, null, error);
}
