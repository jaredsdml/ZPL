namespace Zps.Data.Models.Recibos;

/// <summary>
/// Fila de recibos_checklist_calidad: inspección física de vehículo y producto (formato de
/// calidad y maniobra FOR-OPE-01/02), relación 1:1 con un recibo. Los 21 campos de
/// inspección son texto restringido a SI/NO/N-A (ver <see cref="ValoresPermitidos"/>) en vez
/// de booleanos, porque el formato físico admite "No aplica" además de sí/no; nulo significa
/// "aún no verificado".
/// </summary>
public sealed record ReciboChecklistCalidadModel(
    long? Id,
    long ReciboId,

    // Condiciones del vehículo
    string? VehiculoLimpiezaInterior,
    string? VehiculoSinHoyosGoteras,
    string? VehiculoSinOlores,
    string? VehiculoEstructuraMetalicaIntegra,
    string? VehiculoLibrePlagas,
    string? VehiculoLibreBasura,
    string? VehiculoLibreGrasaQuimicos,
    string? VehiculoLamparasOperando,
    string? VehiculoSujecionFuncional,
    string? VehiculoMedidasAptas,
    string? VehiculoSellosColocados,
    string? VehiculoCertificadoFumigacion,
    bool? VehiculoAptoOperacion,

    // Condiciones del producto
    string? ProductoLibrePlagas,
    string? ProductoLibreSuciedad,
    string? ProductoEmpaqueSinDano,
    string? ProductoMercanciaSinDano,
    string? ProductoEstibasAdecuadas,
    string? ProductoSinPalletsLadeados,
    string? ProductoSobreBase,
    string? ProductoTarimasBuenasCondiciones,
    string? ProductoPlayoUniforme,
    bool? ProductoAptoOperacion,

    // Actividades de reacondicionamiento
    int? ReacondTraspaleoPallets,
    int? ReacondFumigacionPallets,
    int? ReacondCambioPlayoPallets,
    int? ReacondCambioTarimaPallets,
    int? ReacondLimpiezaPallets,

    string? Observaciones,
    string VerificadoPor,
    DateTimeOffset CreadoEn,
    DateTimeOffset ActualizadoEn)
{
    /// <summary>Valores permitidos para los 21 campos de inspección SI/NO/N-A (mismo CHECK que en Neon).</summary>
    public static readonly IReadOnlyCollection<string> ValoresPermitidos = new[] { "SI", "NO", "N/A" };

    /// <summary>Un valor de inspección es válido si es nulo (no verificado) o uno de <see cref="ValoresPermitidos"/>.</summary>
    public static bool EsValorValido(string? valor) => valor is null || ValoresPermitidos.Contains(valor);
}
