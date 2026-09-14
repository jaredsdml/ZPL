namespace Zps.Data.Models.Recibos;

/// <summary>
/// Fila de recibos_transporte_tiempos: carátula de transporte y marcas de tiempo del
/// proceso (formato de calidad y maniobra FOR-OPE-01/02), relación 1:1 con un recibo
/// (recibo_id es UNIQUE en Neon).
/// </summary>
public sealed record ReciboTransporteTiemposModel(
    long? Id,
    long ReciboId,
    string TipoOperacion,
    string? LineaTransporte,
    string? NombreOperador,
    string? PlacasTracto,
    string? PlacasCaja,
    string? Contenedor,
    string? Anden,
    string? Sellos,
    string? SellosColocadosPor,
    string? FolioCliente,
    string? DestinoEntrega,
    string? TipoUnidad,
    string? CapacidadUnidad,
    string? ManiobraEmpresa,
    DateTimeOffset? HoraArribo,
    DateTimeOffset? HoraEnrrampe,
    DateTimeOffset? HoraInicioProceso,
    DateTimeOffset? HoraTerminoProceso,
    DateTimeOffset? HoraCierreSistema,
    DateTimeOffset? HoraEntregaDocMdc,
    DateTimeOffset? HoraEntregaDocOperador,
    string? Observaciones,
    DateTimeOffset CreadoEn,
    DateTimeOffset ActualizadoEn);
