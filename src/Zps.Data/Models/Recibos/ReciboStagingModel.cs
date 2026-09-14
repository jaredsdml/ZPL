namespace Zps.Data.Models.Recibos;

/// <summary>
/// Fila de recibos_staging: cabecera de un recibo/PO en proceso de paletización digital
/// (módulo de paletización y recibo digital). El flujo de Status sigue
/// BORRADOR -> EN_PROCESO -> CONFIRMADO -> IMPRESO, con CANCELADO posible en cualquier
/// punto (mismo CHECK que en Neon).
/// </summary>
public sealed record ReciboStagingModel(
    long? Id,
    string PoNumber,
    string ClientCode,
    string? VendorCode,
    string Status,
    string? Solicitante,
    int TotalPiezasEsperadas,
    int TotalPiezasPaletizadas,
    int TotalTarimas,
    DateTimeOffset CreadoEn,
    DateTimeOffset ActualizadoEn);
