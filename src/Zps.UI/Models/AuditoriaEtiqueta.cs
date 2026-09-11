namespace Zps.UI.Models;

/// <summary>
/// Una etiqueta renderizada por Labelary para revisión visual cuando se generó con la
/// impresora virtual "[VIRTUAL] Previsualizador / Testing" en vez de una Zebra real.
/// </summary>
public sealed record AuditoriaEtiqueta(string Lpn, byte[]? ImagenPng, string? Error);
