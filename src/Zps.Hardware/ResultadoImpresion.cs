namespace Zps.Hardware;

public sealed record ResultadoImpresion(bool Exito, string? Error)
{
    public static ResultadoImpresion Ok() => new(true, null);

    public static ResultadoImpresion Fallo(string error) => new(false, error);
}
