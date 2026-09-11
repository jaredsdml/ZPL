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
    /// <summary>
    /// Grupos de nombres de columna que distintos clientes/plantillas usan para referirse
    /// al mismo dato real (p. ej. AXO mapea "{ATTR}" a la columna "ATTR" en su catálogo,
    /// pero el Excel de piso real trae la columna "ATRIBUTO"). Si el nombre de columna del
    /// mapeo no aparece literalmente en la fila, se prueban sus sinónimos antes de dejar el
    /// token en blanco.
    /// </summary>
    private static readonly string[][] GruposSinonimosColumna =
    {
        new[] { "ATRIBUTO", "ATTR" },
        new[] { "CANTIDAD", "CANT", "QTY" },
    };

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

        // Búsqueda insensible a mayúsculas: los nombres de columna del mapeo y de la fila
        // deberían llegar ya en mayúsculas, pero esto evita tokens en blanco por un simple
        // desajuste de capitalización.
        var datosFilaInsensible = new Dictionary<string, string?>(datosFila, StringComparer.OrdinalIgnoreCase);

        foreach (var (placeholder, nombreColumna) in mapeoColumnas)
        {
            var valor = ObtenerValorConSinonimos(datosFilaInsensible, nombreColumna);
            if (string.IsNullOrEmpty(valor) || string.Equals(valor, "nan", StringComparison.OrdinalIgnoreCase))
            {
                valor = string.Empty;
            }

            zpl = zpl.Replace(placeholder, valor, StringComparison.Ordinal);
        }

        return zpl;
    }

    private static string? ObtenerValorConSinonimos(IReadOnlyDictionary<string, string?> datosFila, string nombreColumna)
    {
        if (datosFila.TryGetValue(nombreColumna, out var valorDirecto) && !string.IsNullOrEmpty(valorDirecto))
        {
            return valorDirecto;
        }

        var grupo = Array.Find(GruposSinonimosColumna, g => Array.Exists(g, s => string.Equals(s, nombreColumna, StringComparison.OrdinalIgnoreCase)));
        if (grupo is not null)
        {
            foreach (var sinonimo in grupo)
            {
                if (datosFila.TryGetValue(sinonimo, out var valorSinonimo) && !string.IsNullOrEmpty(valorSinonimo))
                {
                    return valorSinonimo;
                }
            }
        }

        return valorDirecto;
    }
}
