namespace Zps.Data.Models.Wms;

/// <summary>
/// Línea de una orden de arribo/PO leída de t_po_detail en HighJump (AAD), unida a
/// t_po_master. Qty es la cantidad esperada según el WMS (no la paletizada realmente, que
/// vive en recibos_staging_detalle.cantidad_piezas).
/// </summary>
public sealed record WmsPoDetailModel(
    string PoNumber,
    int LineNumber,
    string ItemNumber,
    decimal Qty,
    string? Uom);
