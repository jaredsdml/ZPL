using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Zps.Data.Models;

namespace Zps.Data.LocalCache;

/// <summary>
/// Caché local offline en SQLite (modo WAL) ubicada en %LOCALAPPDATA%\LogAm\ZPS\zps_cache.db.
/// Guarda una réplica de cat_clientes e historico_impresiones para que la estación pueda
/// leer plantillas/mapeos y reimprimir folios ya generados aunque Neon no esté disponible.
/// No es la fuente de verdad: se sincroniza (upsert) desde Zps.Data contra Neon.
/// </summary>
public sealed class LocalCacheStore
{
    private readonly string _connectionString;

    public string DatabasePath { get; }

    public LocalCacheStore(string? databasePath = null)
    {
        DatabasePath = databasePath ?? ObtenerRutaPorDefecto();
        var directorio = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directorio))
        {
            Directory.CreateDirectory(directorio);
        }

        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString();
    }

    public static string ObtenerRutaPorDefecto()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "LogAm", "ZPS", "zps_cache.db");
    }

    public async Task InicializarAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await AbrirConexionAsync(cancellationToken);

        await EjecutarAsync(connection, null, "PRAGMA journal_mode=WAL;", cancellationToken);

        await EjecutarAsync(connection, null, """
            CREATE TABLE IF NOT EXISTS cat_clientes (
                codigo          TEXT PRIMARY KEY,
                nombre          TEXT NOT NULL,
                activo          INTEGER NOT NULL DEFAULT 1,
                prefijo_folio   TEXT,
                estrategia_lpn  TEXT NOT NULL,
                plantilla_zpl   TEXT,
                mapeo_columnas  TEXT NOT NULL DEFAULT '{}',
                actualizado_en  TEXT NOT NULL
            );
            """, cancellationToken);

        await EjecutarAsync(connection, null, """
            CREATE TABLE IF NOT EXISTS historico_impresiones (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                lpn             TEXT NOT NULL UNIQUE,
                consecutivo     INTEGER,
                tipo            TEXT,
                cliente         TEXT,
                solicitante     TEXT NOT NULL,
                arribo          TEXT NOT NULL,
                fecha_hora      TEXT NOT NULL,
                sku             TEXT,
                lote            TEXT,
                cantidad        TEXT,
                variables_json  TEXT
            );
            """, cancellationToken);

        // Migra cachés locales creadas por una versión anterior de la app (columna
        // "datos_fila" en vez de "variables_json", sin sku/lote/cantidad): CREATE TABLE IF
        // NOT EXISTS no altera una tabla ya existente, así que hay que revisar/ajustar el
        // esquema explícitamente para no romper instalaciones ya en uso.
        await MigrarEsquemaHistoricoAsync(connection, cancellationToken);

        await EjecutarAsync(connection, null,
            "CREATE INDEX IF NOT EXISTS idx_local_historico_cliente ON historico_impresiones (cliente);",
            cancellationToken);
        await EjecutarAsync(connection, null,
            "CREATE INDEX IF NOT EXISTS idx_local_historico_arribo ON historico_impresiones (arribo);",
            cancellationToken);
    }

    /// <summary>Replica (upsert) el catálogo de clientes leído de Neon hacia la caché local.</summary>
    public async Task ReplicarClientesAsync(IEnumerable<ClienteCatalogoRecord> clientes, CancellationToken cancellationToken = default)
    {
        await using var connection = await AbrirConexionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        foreach (var c in clientes)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO cat_clientes (codigo, nombre, activo, prefijo_folio, estrategia_lpn, plantilla_zpl, mapeo_columnas, actualizado_en)
                VALUES ($codigo, $nombre, $activo, $prefijo_folio, $estrategia_lpn, $plantilla_zpl, $mapeo_columnas, $actualizado_en)
                ON CONFLICT (codigo) DO UPDATE SET
                    nombre = excluded.nombre,
                    activo = excluded.activo,
                    prefijo_folio = excluded.prefijo_folio,
                    estrategia_lpn = excluded.estrategia_lpn,
                    plantilla_zpl = excluded.plantilla_zpl,
                    mapeo_columnas = excluded.mapeo_columnas,
                    actualizado_en = excluded.actualizado_en;
                """;
            cmd.Parameters.AddWithValue("$codigo", c.Codigo);
            cmd.Parameters.AddWithValue("$nombre", c.Nombre);
            cmd.Parameters.AddWithValue("$activo", c.Activo ? 1 : 0);
            cmd.Parameters.AddWithValue("$prefijo_folio", (object?)c.PrefijoFolio ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$estrategia_lpn", c.EstrategiaLpn);
            cmd.Parameters.AddWithValue("$plantilla_zpl", (object?)c.PlantillaZpl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$mapeo_columnas", JsonSerializer.Serialize(c.MapeoColumnas));
            cmd.Parameters.AddWithValue("$actualizado_en", c.ActualizadoEn.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Replica (upsert) folios impresos hacia la caché local, para reimpresión sin internet.</summary>
    public async Task ReplicarHistoricoAsync(IEnumerable<HistoricoImpresionRecord> registros, CancellationToken cancellationToken = default)
    {
        await using var connection = await AbrirConexionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        foreach (var r in registros)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO historico_impresiones (lpn, consecutivo, tipo, cliente, solicitante, arribo, fecha_hora, sku, lote, cantidad, variables_json)
                VALUES ($lpn, $consecutivo, $tipo, $cliente, $solicitante, $arribo, $fecha_hora, $sku, $lote, $cantidad, $variables_json)
                ON CONFLICT (lpn) DO UPDATE SET
                    consecutivo = excluded.consecutivo,
                    tipo = excluded.tipo,
                    cliente = excluded.cliente,
                    solicitante = excluded.solicitante,
                    arribo = excluded.arribo,
                    fecha_hora = excluded.fecha_hora,
                    sku = excluded.sku,
                    lote = excluded.lote,
                    cantidad = excluded.cantidad,
                    variables_json = excluded.variables_json;
                """;
            cmd.Parameters.AddWithValue("$lpn", r.Lpn);
            cmd.Parameters.AddWithValue("$consecutivo", (object?)r.Consecutivo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tipo", (object?)r.Tipo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cliente", (object?)r.Cliente ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$solicitante", r.Solicitante);
            cmd.Parameters.AddWithValue("$arribo", r.Arribo);
            cmd.Parameters.AddWithValue("$fecha_hora", r.FechaHora.ToString("O"));
            cmd.Parameters.AddWithValue("$sku", (object?)r.Sku ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lote", (object?)r.Lote ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cantidad", r.Cantidad.HasValue ? r.Cantidad.Value.ToString(CultureInfo.InvariantCulture) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$variables_json", (object?)r.VariablesJson ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Elimina de la caché local los folios indicados (borrado seguro desde Histórico), en espejo de HistoricoService.EliminarAsync en Neon.</summary>
    public async Task EliminarHistoricoAsync(IEnumerable<string> lpns, CancellationToken cancellationToken = default)
    {
        await using var connection = await AbrirConexionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        foreach (var lpn in lpns)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "DELETE FROM historico_impresiones WHERE lpn = $lpn;";
            cmd.Parameters.AddWithValue("$lpn", lpn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClienteCatalogoRecord>> ObtenerClientesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await AbrirConexionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT codigo, nombre, activo, prefijo_folio, estrategia_lpn, plantilla_zpl, mapeo_columnas, actualizado_en
            FROM cat_clientes
            ORDER BY codigo;
            """;

        var resultado = new List<ClienteCatalogoRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var mapeoJson = reader.GetString(6);
            var mapeo = JsonSerializer.Deserialize<Dictionary<string, string>>(mapeoJson) ?? new Dictionary<string, string>();
            resultado.Add(new ClienteCatalogoRecord(
                Codigo: reader.GetString(0),
                Nombre: reader.GetString(1),
                Activo: reader.GetInt64(2) != 0,
                PrefijoFolio: reader.IsDBNull(3) ? null : reader.GetString(3),
                EstrategiaLpn: reader.GetString(4),
                PlantillaZpl: reader.IsDBNull(5) ? null : reader.GetString(5),
                MapeoColumnas: mapeo,
                ActualizadoEn: DateTimeOffset.Parse(reader.GetString(7))));
        }

        return resultado;
    }

    public async Task<IReadOnlyList<HistoricoImpresionRecord>> ObtenerHistoricoPorArriboAsync(
        string cliente, string arribo, CancellationToken cancellationToken = default)
    {
        await using var connection = await AbrirConexionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT lpn, consecutivo, tipo, cliente, solicitante, arribo, fecha_hora, sku, lote, cantidad, variables_json
            FROM historico_impresiones
            WHERE cliente = $cliente AND arribo = $arribo
            ORDER BY fecha_hora;
            """;
        cmd.Parameters.AddWithValue("$cliente", cliente);
        cmd.Parameters.AddWithValue("$arribo", arribo);

        var resultado = new List<HistoricoImpresionRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            resultado.Add(LeerRegistro(reader));
        }

        return resultado;
    }

    /// <summary>
    /// Equivalente local (sin Neon) de HistoricoService.BuscarAsync, para cuando la estación
    /// no tiene internet: mismo criterio de búsqueda (LPN, cliente, arribo, solicitante,
    /// tipo, consecutivo, SKU y lote).
    /// </summary>
    public async Task<IReadOnlyList<HistoricoImpresionRecord>> BuscarHistoricoAsync(
        string? filtro, int limite = 500, CancellationToken cancellationToken = default)
    {
        await using var connection = await AbrirConexionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT lpn, consecutivo, tipo, cliente, solicitante, arribo, fecha_hora, sku, lote, cantidad, variables_json
            FROM historico_impresiones
            WHERE $filtro = ''
               OR lpn LIKE '%' || $filtro || '%'
               OR cliente LIKE '%' || $filtro || '%'
               OR arribo LIKE '%' || $filtro || '%'
               OR solicitante LIKE '%' || $filtro || '%'
               OR tipo LIKE '%' || $filtro || '%'
               OR CAST(consecutivo AS TEXT) LIKE '%' || $filtro || '%'
               OR sku LIKE '%' || $filtro || '%'
               OR lote LIKE '%' || $filtro || '%'
            ORDER BY fecha_hora DESC
            LIMIT $limite;
            """;
        cmd.Parameters.AddWithValue("$filtro", filtro ?? string.Empty);
        cmd.Parameters.AddWithValue("$limite", limite);

        var resultado = new List<HistoricoImpresionRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            resultado.Add(LeerRegistro(reader));
        }

        return resultado;
    }

    private static HistoricoImpresionRecord LeerRegistro(SqliteDataReader reader) => new(
        Lpn: reader.GetString(0),
        Consecutivo: reader.IsDBNull(1) ? null : reader.GetInt32(1),
        Tipo: reader.IsDBNull(2) ? null : reader.GetString(2),
        Cliente: reader.IsDBNull(3) ? null : reader.GetString(3),
        Solicitante: reader.GetString(4),
        Arribo: reader.GetString(5),
        FechaHora: DateTimeOffset.Parse(reader.GetString(6)),
        Sku: reader.IsDBNull(7) ? null : reader.GetString(7),
        Lote: reader.IsDBNull(8) ? null : reader.GetString(8),
        Cantidad: reader.IsDBNull(9) ? null : decimal.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
        VariablesJson: reader.IsDBNull(10) ? null : reader.GetString(10));

    private static async Task MigrarEsquemaHistoricoAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var columnas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(historico_impresiones);";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columnas.Add(reader.GetString(1)); // columna "name" de PRAGMA table_info
            }
        }

        if (columnas.Contains("datos_fila") && !columnas.Contains("variables_json"))
        {
            await EjecutarAsync(connection, null,
                "ALTER TABLE historico_impresiones RENAME COLUMN datos_fila TO variables_json;",
                cancellationToken);
            columnas.Remove("datos_fila");
            columnas.Add("variables_json");
        }

        if (!columnas.Contains("variables_json"))
        {
            await EjecutarAsync(connection, null, "ALTER TABLE historico_impresiones ADD COLUMN variables_json TEXT;", cancellationToken);
        }

        if (!columnas.Contains("sku"))
        {
            await EjecutarAsync(connection, null, "ALTER TABLE historico_impresiones ADD COLUMN sku TEXT;", cancellationToken);
        }

        if (!columnas.Contains("lote"))
        {
            await EjecutarAsync(connection, null, "ALTER TABLE historico_impresiones ADD COLUMN lote TEXT;", cancellationToken);
        }

        if (!columnas.Contains("cantidad"))
        {
            await EjecutarAsync(connection, null, "ALTER TABLE historico_impresiones ADD COLUMN cantidad TEXT;", cancellationToken);
        }
    }

    private async Task<SqliteConnection> AbrirConexionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task EjecutarAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
