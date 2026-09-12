using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using Zps.Data.Models;

namespace Zps.Data;

/// <summary>
/// Administración en vivo de plantillas ZPL (plantillas_zpl) en Neon: permite dar de alta o
/// modificar el ZPL de un cliente, enlazado por el nombre de hoja de Excel, sin publicar un
/// release. Usada tanto por la ventana de administración (AdminPlantillasWindow) como por el
/// Generador Principal para resolver la plantilla activa de la hoja seleccionada.
/// </summary>
public sealed class PlantillasService
{
    private readonly NpgsqlDataSource _dataSource;

    public PlantillasService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<PlantillaZplRecord>> ObtenerTodasAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, hoja_excel, nombre_cliente, codigo_zpl, activo, fecha_modificacion
            FROM plantillas_zpl
            ORDER BY hoja_excel;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var resultado = new List<PlantillaZplRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            resultado.Add(LeerRegistro(reader));
        }

        return resultado;
    }

    /// <summary>
    /// Busca la plantilla activa enlazada a una hoja de Excel, comparando sin distinguir
    /// mayúsculas/minúsculas (la hoja del libro puede tener una capitalización distinta a la
    /// que tecleó el administrador). Devuelve null si no hay ninguna registrada o activa.
    /// </summary>
    public async Task<PlantillaZplRecord?> ObtenerPorHojaAsync(string hojaExcel, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, hoja_excel, nombre_cliente, codigo_zpl, activo, fecha_modificacion
            FROM plantillas_zpl
            WHERE LOWER(hoja_excel) = LOWER(@hoja) AND activo = TRUE;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("hoja", hojaExcel);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? LeerRegistro(reader) : null;
    }

    /// <summary>Alta o edición por upsert (clave natural: hoja_excel).</summary>
    public async Task GuardarOActualizarAsync(PlantillaZplRecord plantilla, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO plantillas_zpl (hoja_excel, nombre_cliente, codigo_zpl, activo, fecha_modificacion)
            VALUES (@hoja, @nombre, @codigo, @activo, now())
            ON CONFLICT (hoja_excel) DO UPDATE SET
                nombre_cliente = excluded.nombre_cliente,
                codigo_zpl = excluded.codigo_zpl,
                activo = excluded.activo,
                fecha_modificacion = now();
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("hoja", plantilla.HojaExcel);
        command.Parameters.AddWithValue("nombre", plantilla.NombreCliente);
        command.Parameters.AddWithValue("codigo", plantilla.CodigoZpl);
        command.Parameters.AddWithValue("activo", plantilla.Activo);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task EliminarAsync(string hojaExcel, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("DELETE FROM plantillas_zpl WHERE hoja_excel = @hoja;", connection);
        command.Parameters.AddWithValue("hoja", hojaExcel);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Siembra plantillas_zpl a partir de las plantillas legacy ya activas en cat_clientes
    /// (MYC, AXO, MNS/MINISO, PRM, CEVA, DRL, etc.), traduciendo cada placeholder de una
    /// sola llave ("{SKU}", "{ATTR}", "{PEDIMENTO}"...) — resuelto vía el mapeo_columnas
    /// propio de cada cliente — a su tag de doble llave equivalente ("{{SKU}}",
    /// "{{ATTR}}", "{{PEDIMENTO}}"...) que entiende ZplPlantillaDinamicaEngine, y
    /// "{INDICE}"/"{TOTAL}" a "{{TARIMA_ACTUAL}}"/"{{TARIMA_TOTAL}}". Sin <paramref
    /// name="forzar"/>, no hace nada si plantillas_zpl ya tiene filas (para no pisar ediciones
    /// ya hechas desde el panel de administración); con forzar=true (botón "Restaurar /
    /// Importar Plantillas Base"), siempre reimporta y sobrescribe por hoja_excel.
    /// Devuelve las plantillas insertadas/actualizadas, para que el llamador las replique
    /// también en la caché local.
    /// </summary>
    public async Task<IReadOnlyList<PlantillaZplRecord>> MigrarPlantillasLegacySiVacioAsync(
        bool forzar = false, CancellationToken cancellationToken = default)
    {
        await using (var connection = await _dataSource.OpenConnectionAsync(cancellationToken))
        {
            if (!forzar)
            {
                await using var cmdConteo = new NpgsqlCommand("SELECT COUNT(*) FROM plantillas_zpl;", connection);
                var total = (long)(await cmdConteo.ExecuteScalarAsync(cancellationToken))!;
                if (total > 0)
                {
                    return Array.Empty<PlantillaZplRecord>();
                }
            }
        }

        const string sqlClientes = """
            SELECT codigo, nombre, mapeo_columnas, plantilla_zpl
            FROM cat_clientes
            WHERE plantilla_zpl IS NOT NULL AND plantilla_zpl <> '';
            """;

        var candidatos = new List<PlantillaZplRecord>();
        await using (var connection = await _dataSource.OpenConnectionAsync(cancellationToken))
        await using (var cmdClientes = new NpgsqlCommand(sqlClientes, connection))
        await using (var reader = await cmdClientes.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var codigo = reader.GetString(0);
                var nombre = reader.GetString(1);
                var mapeoJson = reader.GetString(2);
                var mapeo = JsonSerializer.Deserialize<Dictionary<string, string>>(mapeoJson) ?? new Dictionary<string, string>();
                var plantillaOriginal = reader.GetString(3);

                candidatos.Add(new PlantillaZplRecord(
                    Id: null,
                    HojaExcel: codigo,
                    NombreCliente: nombre,
                    CodigoZpl: TraducirPlantillaLegacy(plantillaOriginal, mapeo),
                    Activo: true,
                    FechaModificacion: DateTimeOffset.UtcNow));
            }
        }

        foreach (var plantilla in candidatos)
        {
            await GuardarOActualizarAsync(plantilla, cancellationToken);
        }

        return candidatos;
    }

    private static readonly Regex PlaceholderLegacyRegex = new(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Traduce en una sola pasada cada placeholder "{TOKEN}" del ZPL legacy a su tag
    /// "{{COLUMNA}}" equivalente, usando el mapeo_columnas propio del cliente para saber a
    /// qué columna real apuntaba cada token (p. ej. "{ATTR}" -> "ATRIBUTO" en CEVA, pero
    /// "{ATTR}" -> "ATTR" en AXO). Un token sin mapeo conocido se deja tal cual (una sola
    /// llave): no hay forma segura de adivinar a qué columna se refería.
    /// </summary>
    private static string TraducirPlantillaLegacy(string plantillaOriginal, IReadOnlyDictionary<string, string> mapeoColumnas)
    {
        var columnaPorToken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (placeholderViejo, columna) in mapeoColumnas)
        {
            columnaPorToken[placeholderViejo.Trim('{', '}')] = columna;
        }

        return PlaceholderLegacyRegex.Replace(plantillaOriginal, match =>
        {
            var token = match.Groups[1].Value;

            if (string.Equals(token, "INDICE", StringComparison.OrdinalIgnoreCase))
            {
                return "{{TARIMA_ACTUAL}}";
            }

            if (string.Equals(token, "TOTAL", StringComparison.OrdinalIgnoreCase))
            {
                return "{{TARIMA_TOTAL}}";
            }

            return columnaPorToken.TryGetValue(token, out var columna)
                ? $"{{{{{columna.ToUpperInvariant()}}}}}"
                : match.Value;
        });
    }

    private static PlantillaZplRecord LeerRegistro(NpgsqlDataReader reader) => new(
        Id: reader.GetInt32(reader.GetOrdinal("id")),
        HojaExcel: reader.GetString(reader.GetOrdinal("hoja_excel")),
        NombreCliente: reader.GetString(reader.GetOrdinal("nombre_cliente")),
        CodigoZpl: reader.GetString(reader.GetOrdinal("codigo_zpl")),
        Activo: reader.GetBoolean(reader.GetOrdinal("activo")),
        FechaModificacion: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("fecha_modificacion")));
}
