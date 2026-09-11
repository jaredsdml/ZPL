namespace Zps.Data.Models;

/// <summary>
/// Fila de historico_impresiones: un folio efectivamente impreso, junto con sus metadatos
/// operativos promovidos a columnas propias (Sku, Lote, Cantidad — para poder mostrarlos y
/// buscarlos directamente en la grilla de Histórico) y la fila completa de datos de origen
/// (VariablesJson, JSONB en Neon: incluye ATRIBUTO, CAJAS, CATEGORIA, ITEM, etc., además de
/// una copia de LPN/SKU/LOTE/CANTIDAD) — espejo combinado de la tabla 'etiquetas' de
/// logam_sistema.db y de las tablas por-cliente de logam_reimpresiones.db en el original.
/// </summary>
public sealed record HistoricoImpresionRecord(
    string Lpn,
    int? Consecutivo,
    string? Tipo,
    string? Cliente,
    string Solicitante,
    string Arribo,
    DateTimeOffset FechaHora,
    string? Sku,
    string? Lote,
    decimal? Cantidad,
    string? VariablesJson);
