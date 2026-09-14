namespace Zps.Data.Models.Wms;

/// <summary>
/// Filtros de búsqueda para AadWmsService.BuscarInventarioAsync (t_stored_item unida a
/// t_hu_master y t_item_master en HighJump/AAD). Un campo de texto nulo o en blanco no
/// filtra; Lpn/Sku/Lote/Ubicacion filtran por coincidencia parcial, Cliente por igualdad
/// exacta (client_code). SoloDisponibles=true excluye filas sin piezas disponibles
/// (CantidadTotal - CantidadRetenida &lt;= 0).
/// </summary>
public sealed record InventarioFiltrosModel(
    string? Lpn,
    string? Sku,
    string? Lote,
    string? Ubicacion,
    string? Cliente,
    bool SoloDisponibles)
{
    /// <summary>
    /// Tope de filas a traer (TOP). Nulo o 0 solo se traduce en "sin tope" si al menos uno
    /// de Lpn/Sku/Lote/Ubicacion viene informado (una búsqueda ya acotada); sin ningún
    /// filtro de texto, AadWmsService igual aplica la salvaguarda dura de 500 para no
    /// descargar el inventario completo del WMS por accidente.
    /// </summary>
    public int? LimiteMaximo { get; init; } = 500;
}
