using System.Globalization;
using System.Text.RegularExpressions;

namespace Zps.Core;

/// <summary>
/// Sustituye placeholders "{{TAG}}" (doble llave, distinto del formato "{PLACEHOLDER}" de
/// ZplTemplateEngine/mapeo_columnas) en el código ZPL de una plantilla administrada en vivo
/// desde plantillas_zpl (Neon), para poder dar de alta o modificar un cliente sin publicar
/// un release. Un puñado de tags son datos calculados por la app, no columnas del Excel
/// (LPN, CONSECUTIVO, TARIMA_ACTUAL, TARIMA_TOTAL, ARRIBO, SOLICITANTE) y CANTIDAD tiene un
/// fallback de conveniencia a la columna QTY; cualquier otro "{{TAG}}" — SKU, LOTE, CAJAS, o
/// una columna propia de un cliente nuevo (p. ej. {{ATTR}}, {{PEDIMENTO}}, {{NUMERO}}) — se
/// resuelve por búsqueda directa (insensible a mayúsculas) contra la fila. Esto es lo que
/// permite dar de alta un cliente con columnas completamente distintas sin tocar código.
/// </summary>
public static class ZplPlantillaDinamicaEngine
{
    private static readonly Regex TagRegex = new(@"\{\{([A-Za-z0-9_]+)\}\}", RegexOptions.Compiled);

    public static string Generar(
        string codigoZpl,
        IReadOnlyDictionary<string, string?> datosFila,
        string? lpn,
        int? consecutivo,
        int tarimaActual,
        int tarimaTotal,
        string? arribo,
        string? solicitante)
    {
        ArgumentNullException.ThrowIfNull(codigoZpl);
        ArgumentNullException.ThrowIfNull(datosFila);

        var zpl = codigoZpl.Contains("^CI28", StringComparison.Ordinal)
            ? codigoZpl
            : codigoZpl.Replace("^XA", "^XA^CI28", StringComparison.Ordinal);

        // Búsqueda insensible a mayúsculas: los nombres de columna de la fila deberían
        // llegar ya en mayúsculas, pero esto evita tokens en blanco por un simple desajuste
        // de capitalización.
        var datosInsensible = new Dictionary<string, string?>(datosFila, StringComparer.OrdinalIgnoreCase);

        return TagRegex.Replace(zpl, match => ResolverTag(
            match.Groups[1].Value, datosInsensible, lpn, consecutivo, tarimaActual, tarimaTotal, arribo, solicitante));
    }

    private static string ResolverTag(
        string tag,
        IReadOnlyDictionary<string, string?> datosFila,
        string? lpn,
        int? consecutivo,
        int tarimaActual,
        int tarimaTotal,
        string? arribo,
        string? solicitante)
    {
        switch (tag.ToUpperInvariant())
        {
            case "LPN":
                return lpn ?? string.Empty;
            case "CONSECUTIVO":
                return consecutivo?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            case "TARIMA_ACTUAL":
                return tarimaActual.ToString(CultureInfo.InvariantCulture);
            case "TARIMA_TOTAL":
                return tarimaTotal.ToString(CultureInfo.InvariantCulture);
            case "ARRIBO":
                return arribo ?? string.Empty;
            case "SOLICITANTE":
                return solicitante ?? string.Empty;
            case "CANTIDAD":
                var cantidad = ObtenerColumna(datosFila, "CANTIDAD");
                return string.IsNullOrEmpty(cantidad) ? ObtenerColumna(datosFila, "QTY") : cantidad;
            default:
                // Cualquier otro tag (SKU, LOTE, CAJAS, o una columna propia de un cliente
                // nuevo) se resuelve directo contra la fila, sin necesitar un mapeo previo.
                return ObtenerColumna(datosFila, tag);
        }
    }

    private static string ObtenerColumna(IReadOnlyDictionary<string, string?> datosFila, string nombreColumna)
    {
        if (!datosFila.TryGetValue(nombreColumna, out var valor) ||
            string.IsNullOrEmpty(valor) ||
            string.Equals(valor, "nan", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // Trim: las celdas de Excel a veces traen espacios de más al inicio/final, que de
        // otro modo quedarían impresos tal cual en la etiqueta.
        return valor.Trim();
    }
}
