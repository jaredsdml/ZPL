namespace Zps.Core;

/// <summary>
/// Sustituye los placeholders de una plantilla ZPL cruda por los valores de una fila de
/// datos. Puerto fiel de generar_zpl_final (app_centralizada.py): activa la página de
/// códigos UTF-8 (^CI28) si la plantilla no la trae, reemplaza {INDICE}/{TOTAL} y luego
/// cada placeholder del mapeo de columnas (p. ej. "{SKU}" -> valor de la columna SKU),
/// tratando "nan" (de datos numéricos vacíos de origen Excel/pandas-like) como cadena vacía.
/// </summary>
public static class ZplTemplateEngine
{
    public static string Generar(
        string plantillaZpl,
        IReadOnlyDictionary<string, string> mapeoColumnas,
        IReadOnlyDictionary<string, string?> datosFila,
        int indiceActual = 1,
        int totalFilas = 1)
    {
        ArgumentNullException.ThrowIfNull(plantillaZpl);
        ArgumentNullException.ThrowIfNull(mapeoColumnas);
        ArgumentNullException.ThrowIfNull(datosFila);

        var zpl = plantillaZpl.Contains("^CI28", StringComparison.Ordinal)
            ? plantillaZpl
            : plantillaZpl.Replace("^XA", "^XA^CI28", StringComparison.Ordinal);

        zpl = zpl
            .Replace("{INDICE}", indiceActual.ToString(), StringComparison.Ordinal)
            .Replace("{TOTAL}", totalFilas.ToString(), StringComparison.Ordinal);

        foreach (var (placeholder, nombreColumna) in mapeoColumnas)
        {
            var valor = datosFila.TryGetValue(nombreColumna, out var v) ? v : null;
            if (string.IsNullOrEmpty(valor) || string.Equals(valor, "nan", StringComparison.OrdinalIgnoreCase))
            {
                valor = string.Empty;
            }

            zpl = zpl.Replace(placeholder, valor, StringComparison.Ordinal);
        }

        return zpl;
    }
}
