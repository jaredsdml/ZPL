namespace Zps.Data.Models;

/// <summary>
/// Fila de historico_impresiones: un folio efectivamente impreso, junto con sus metadatos
/// operativos promovidos a columnas propias (Sku, Lote, Cantidad, Cajas — para poder
/// mostrarlos y buscarlos directamente en la grilla de Histórico) y la fila completa de
/// datos de origen (VariablesJson, JSONB en Neon: incluye ATRIBUTO, CATEGORIA, ITEM, etc.,
/// además de una copia de LPN/SKU/LOTE/CANTIDAD/CAJAS) — espejo combinado de la tabla
/// 'etiquetas' de logam_sistema.db y de las tablas por-cliente de logam_reimpresiones.db
/// en el original. Cajas es texto (no numérico) porque algunos clientes (p. ej. AXO)
/// capturan valores no numéricos como "NV" junto con cantidades reales.
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
    string? Cajas,
    string? VariablesJson);
