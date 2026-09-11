namespace Zps.Core.Models;

/// <summary>
/// Resultado de validar la combinación Categoría/Motivo de una fila MINISO,
/// espejo del bloque de validación dentro de GestorLPN.generar_folios en app_centralizada.py.
/// </summary>
/// <param name="EsValido">True si la fila cumple las reglas de categoría/motivo.</param>
/// <param name="TipoSecuencia">Tipo de secuencia resultante cuando EsValido es true; null en caso contrario.</param>
/// <param name="Motivo">Motivo numérico extraído de la columna ITEM/MOTIVO; null si no se pudo extraer.</param>
/// <param name="MensajeError">Mensaje de error listo para mostrar al usuario cuando EsValido es false.</param>
public sealed record MinisoValidationResult(
    bool EsValido,
    TipoSecuenciaLpn? TipoSecuencia,
    int? Motivo,
    string? MensajeError)
{
    public static MinisoValidationResult Valido(TipoSecuenciaLpn tipo, int? motivo = null) =>
        new(true, tipo, motivo, null);

    public static MinisoValidationResult Invalido(string mensajeError, int? motivo = null) =>
        new(false, null, motivo, mensajeError);
}
