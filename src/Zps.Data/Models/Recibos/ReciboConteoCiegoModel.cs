namespace Zps.Data.Models.Recibos;

/// <summary>
/// Fila de recibos_conteo_ciego_pallets: resumen físico de tarimas por tipo (formato de
/// calidad y maniobra FOR-OPE-01/02), relación 1:1 con un recibo. TotalPallets es una
/// columna generada en Neon (suma de los 6 conteos): se recibe calculada desde la base de
/// datos y nunca se envía en el INSERT/UPDATE.
/// </summary>
public sealed record ReciboConteoCiegoModel(
    long? Id,
    long ReciboId,
    int TarimaEstandar,
    int TarimaChep,
    int TarimaEuropalet,
    int TarimaTagon,
    int TarimaPlastico,
    int Granel,
    int TotalPallets,
    DateTimeOffset CreadoEn,
    DateTimeOffset ActualizadoEn);
