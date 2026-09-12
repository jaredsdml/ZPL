namespace Zps.Data.Models;

/// <summary>
/// Un cambio individual a un campo operativo de historico_impresiones (p. ej. al editar
/// Sku/Lote/Cantidad desde la pestaña Histórico), para registrar en historico_cambios_log
/// junto con el valor anterior y el nuevo.
/// </summary>
public sealed record CambioCampo(string Campo, string? ValorAnterior, string? ValorNuevo);
