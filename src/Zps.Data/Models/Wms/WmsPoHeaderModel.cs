namespace Zps.Data.Models.Wms;

/// <summary>
/// Cabecera de una orden de arribo/PO leída de t_po_master en HighJump (AAD, SQL Server,
/// solo lectura vía AadWmsService). Alimenta al módulo de paletización y recibo digital con
/// datos reales del WMS para no capturar a mano lo que ya existe ahí.
/// </summary>
public sealed record WmsPoHeaderModel(
    string PoNumber,
    string? ClientCode,
    string? VendorCode,
    DateTime CreateDate,
    string Status);
