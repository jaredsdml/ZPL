namespace Zps.Data.Models.Recibos;

/// <summary>
/// Fila de recibos_staging_cambios_log: auditoría de una edición a la cabecera de un
/// recibo ('RECIBO_CABECERA') o a una de sus tarimas ('PALLET_DETALLE'), con el valor
/// anterior/nuevo del campo y el motivo de la corrección. Mismo patrón que
/// historico_cambios_log, usado para poder corregir errores humanos (LPN, placas, SKU,
/// lote, cantidades, anulaciones) sin perder rastro de qué cambió, quién y por qué.
/// </summary>
public sealed record ReciboCambioLogModel(
    long? Id,
    long ReciboId,
    long? DetalleId,
    string EntidadModificada,
    string CampoModificado,
    string? ValorAnterior,
    string? ValorNuevo,
    string? MotivoCambio,
    string Usuario,
    DateTimeOffset FechaCambio);
