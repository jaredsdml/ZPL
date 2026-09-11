using System.Text.Json;
using Npgsql;
using Zps.Data.Models;

namespace Zps.Data;

/// <summary>
/// Lectura del catálogo de clientes (cat_clientes) desde Neon. Sustituye a
/// cargar_base_conocimiento() / CONFIG_CLIENTES (config_clientes.json local) del original.
/// </summary>
public sealed class CatalogoClientesService
{
    private readonly NpgsqlDataSource _dataSource;

    public CatalogoClientesService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<ClienteCatalogoRecord>> ObtenerActivosAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT codigo, nombre, activo, prefijo_folio, estrategia_lpn, plantilla_zpl, mapeo_columnas, actualizado_en
            FROM cat_clientes
            WHERE activo = TRUE
            ORDER BY codigo;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var resultado = new List<ClienteCatalogoRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var mapeoJson = reader.GetString(reader.GetOrdinal("mapeo_columnas"));
            var mapeo = JsonSerializer.Deserialize<Dictionary<string, string>>(mapeoJson)
                ?? new Dictionary<string, string>();

            resultado.Add(new ClienteCatalogoRecord(
                Codigo: reader.GetString(reader.GetOrdinal("codigo")),
                Nombre: reader.GetString(reader.GetOrdinal("nombre")),
                Activo: reader.GetBoolean(reader.GetOrdinal("activo")),
                PrefijoFolio: reader.IsDBNull(reader.GetOrdinal("prefijo_folio")) ? null : reader.GetString(reader.GetOrdinal("prefijo_folio")),
                EstrategiaLpn: reader.GetString(reader.GetOrdinal("estrategia_lpn")),
                PlantillaZpl: reader.IsDBNull(reader.GetOrdinal("plantilla_zpl")) ? null : reader.GetString(reader.GetOrdinal("plantilla_zpl")),
                MapeoColumnas: mapeo,
                ActualizadoEn: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("actualizado_en"))));
        }

        return resultado;
    }
}
