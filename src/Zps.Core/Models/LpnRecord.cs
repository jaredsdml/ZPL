namespace Zps.Core.Models;

/// <summary>
/// Representa un folio LPN generado o registrado, espejo de la fila de la tabla
/// 'etiquetas' en logam_sistema.db / 'historico_impresiones' en Neon.
/// </summary>
public sealed record LpnRecord(
    string Lpn,
    int? Consecutivo,
    TipoSecuenciaLpn Tipo,
    string? Cliente,
    string Solicitante,
    string Arribo,
    DateTimeOffset FechaHora);
