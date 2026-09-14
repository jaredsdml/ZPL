namespace Zps.Data.Models.Wms;

/// <summary>
/// Una fila de inventario leída de t_stored_item, unida a t_hu_master (por hu_id — en
/// HighJump la unidad de manejo/HU ES la tarima física, así que Lpn es t_hu_master.hu_id) y,
/// con LEFT JOIN, a t_item_master (por item_number — de ahí salen Descripcion y Cliente,
/// ya que ni t_stored_item ni t_hu_master tienen client_code propio).
/// </summary>
public sealed record InventarioItemModel(
    string Lpn,
    string Sku,
    string? Descripcion,
    string? Lote,
    string Ubicacion,
    string Cliente,
    int CantidadTotal,
    int CantidadRetenida,
    int CantidadDisponible,
    DateTime? Caducidad,
    string EstatusTarima);
