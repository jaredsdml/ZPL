namespace Zps.Data.Models.Recibos;

/// <summary>
/// Fila de recibos_staging_detalle: una tarima virtual (pallet) dentro de un recibo. Varias
/// filas pueden compartir el mismo ConsecutivoTarima cuando un mismo pallet físico mezcla
/// más de un Item/SKU. Activo=false es el borrado lógico usado para dar de baja una tarima
/// sin perder trazabilidad (ver recibos_staging_cambios_log).
/// </summary>
public sealed record ReciboStagingDetalleModel(
    long? Id,
    long ReciboId,
    string ItemNumber,
    string? Descripcion,
    string? Lote,
    string? Atributo,
    int CantidadPiezas,
    int? CantidadCajas,
    bool EsPnc,
    string? HuId,
    string? TipoTarima,
    int ConsecutivoTarima,
    bool Activo,
    string CreadoPor,
    string? ModificadoPor,
    DateTimeOffset CreadoEn,
    DateTimeOffset ActualizadoEn);
