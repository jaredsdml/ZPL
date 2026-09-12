// Herramienta de migración de un solo uso: vuelca el histórico real de logam_sistema.db
// (tabla 'etiquetas') + logam_reimpresiones.db (tablas por cliente con SKU/Lote/Cantidad)
// hacia historico_impresiones en Neon, y ajusta secuencias_lpn al consecutivo real más alto
// por tipo, para que la numeración nueva continúe sin choques con el histórico del piso.
//
// No forma parte de Zps.UI: es un ejecutable aparte, para correr una vez y desechar.

using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Npgsql;
using NpgsqlTypes;
using Zps.Core;
using Zps.Core.Models;
using Zps.Data;
using Zps.Data.Models;

var raizRepo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var rutaSistema = args.Length > 0 ? args[0] : Path.Combine(raizRepo, "logam_sistema.db");
var rutaReimpresiones = args.Length > 1 ? args[1] : Path.Combine(raizRepo, "logam_reimpresiones.db");

// El reloj del piso graba fecha_hora en hora local sin zona horaria; el offset real
// (confirmado por el bug de Npgsql visto antes en este mismo proyecto) es México
// Central, -06:00, sin horario de verano.
var offsetPiso = TimeSpan.FromHours(-6);

Console.WriteLine($"logam_sistema.db:        {rutaSistema}");
Console.WriteLine($"logam_reimpresiones.db:  {rutaReimpresiones}");

if (!File.Exists(rutaSistema) || !File.Exists(rutaReimpresiones))
{
    Console.WriteLine("ERROR: no se encontraron ambos archivos SQLite en las rutas indicadas.");
    return 1;
}

// ---------------------------------------------------------------------------------
// 1) Leer logam_sistema.db (etiquetas) — la lista maestra de folios.
// ---------------------------------------------------------------------------------
var etiquetas = new List<Etiqueta>();
var etiquetasSinLpn = 0;

using (var conn = new SqliteConnection($"Data Source={rutaSistema};Mode=ReadOnly"))
{
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT lpn, consecutivo, tipo, solicitante, arribo, fecha_hora FROM etiquetas;";
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        var lpn = reader.IsDBNull(0) ? null : reader.GetString(0);
        if (string.IsNullOrWhiteSpace(lpn))
        {
            etiquetasSinLpn++;
            continue;
        }

        etiquetas.Add(new Etiqueta(
            Lpn: lpn.Trim(),
            Consecutivo: reader.IsDBNull(1) ? 0L : reader.GetInt64(1),
            Tipo: reader.IsDBNull(2) ? "" : reader.GetString(2),
            Solicitante: reader.IsDBNull(3) ? "" : reader.GetString(3),
            Arribo: reader.IsDBNull(4) ? "" : reader.GetString(4),
            FechaHoraTexto: reader.IsDBNull(5) ? "" : reader.GetString(5)));
    }
}

Console.WriteLine($"etiquetas leídas: {etiquetas.Count} (descartadas por LPN vacío: {etiquetasSinLpn})");

// ---------------------------------------------------------------------------------
// 2) Leer logam_reimpresiones.db — solo las tablas que realmente tienen una columna LPN
//    (descarta tablas ajenas como PNC/UBICACION/HOJA1, que no son respaldos por cliente).
// ---------------------------------------------------------------------------------
var candidatosPorLpn = new Dictionary<string, List<(string Tabla, Dictionary<string, string?> Fila)>>(StringComparer.OrdinalIgnoreCase);
var tablasIncluidas = new List<string>();
var tablasOmitidas = new List<string>();

using (var conn = new SqliteConnection($"Data Source={rutaReimpresiones};Mode=ReadOnly"))
{
    conn.Open();

    var tablas = new List<string>();
    using (var cmdTablas = conn.CreateCommand())
    {
        cmdTablas.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
        using var reader = cmdTablas.ExecuteReader();
        while (reader.Read())
        {
            tablas.Add(reader.GetString(0));
        }
    }

    foreach (var tabla in tablas)
    {
        var columnas = new List<string>();
        using (var cmdInfo = conn.CreateCommand())
        {
            cmdInfo.CommandText = $"PRAGMA table_info(\"{tabla}\");";
            using var reader = cmdInfo.ExecuteReader();
            while (reader.Read())
            {
                columnas.Add(reader.GetString(1));
            }
        }

        var columnaLpn = columnas.FirstOrDefault(c => string.Equals(c, "LPN", StringComparison.OrdinalIgnoreCase));
        if (columnaLpn is null)
        {
            tablasOmitidas.Add(tabla);
            continue;
        }

        tablasIncluidas.Add(tabla);
        using var cmdDatos = conn.CreateCommand();
        cmdDatos.CommandText = $"SELECT * FROM \"{tabla}\";";
        using var lector = cmdDatos.ExecuteReader();
        var indiceLpn = columnas.FindIndex(c => string.Equals(c, "LPN", StringComparison.OrdinalIgnoreCase));

        while (lector.Read())
        {
            var lpnCrudo = lector.IsDBNull(indiceLpn) ? null : lector.GetValue(indiceLpn)?.ToString();
            if (string.IsNullOrWhiteSpace(lpnCrudo))
            {
                continue;
            }

            var fila = new Dictionary<string, string?>();
            for (var i = 0; i < columnas.Count; i++)
            {
                if (columnas[i].Contains("NO EDITAR", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                fila[columnas[i].Trim().ToUpperInvariant()] = lector.IsDBNull(i) ? null : lector.GetValue(i)?.ToString();
            }

            var clave = lpnCrudo.Trim().ToUpperInvariant();
            if (!candidatosPorLpn.TryGetValue(clave, out var lista))
            {
                lista = new List<(string, Dictionary<string, string?>)>();
                candidatosPorLpn[clave] = lista;
            }

            lista.Add((tabla, fila));
        }
    }
}

var colisiones = candidatosPorLpn.Count(kv => kv.Value.Select(c => c.Tabla).Distinct().Count() > 1);

Console.WriteLine($"Tablas de cliente incluidas (tienen columna LPN): {string.Join(", ", tablasIncluidas)}");
Console.WriteLine($"Tablas omitidas (sin columna LPN, no son respaldos por cliente): {string.Join(", ", tablasOmitidas)}");
Console.WriteLine($"LPNs distintos en tablas de respaldo: {candidatosPorLpn.Count} (con colisión entre 2+ tablas: {colisiones})");

// ---------------------------------------------------------------------------------
// 3) Cruzar, normalizar y preparar los registros a insertar.
// ---------------------------------------------------------------------------------
var aliasCliente = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    // Convención operativa del piso vs. código real en cat_clientes (Neon): el
    // régimen MINISO se identifica en el piso por el prefijo "MNS", pero el cliente
    // registrado en el catálogo se llama "MINISO". Sin este alias, las ~5,000 filas
    // de MNS quedarían con un cliente inexistente y la reimpresión fallaría en silencio.
    ["MNS"] = "MINISO",
};

var registros = new List<HistoricoImpresionRecord>();
var conMetadatos = 0;
var sinMetadatos = 0;

foreach (var etiqueta in etiquetas)
{
    var tipoNormalizado = NormalizarTipo(etiqueta.Tipo);
    var consecutivoSeguro = etiqueta.Consecutivo is >= int.MinValue and <= int.MaxValue
        ? (int?)etiqueta.Consecutivo
        : null; // valores fuera de rango de int4 (basura de escaneos MANUAL_EXTERNO): se preserva el LPN, no el número.

    var fechaUtc = ConvertirAUtc(etiqueta.FechaHoraTexto, offsetPiso);

    string? sku = null, lote = null, cajas = null, variablesJson;
    decimal? cantidad = null;
    string cliente;

    if (candidatosPorLpn.TryGetValue(etiqueta.Lpn, out var candidatos))
    {
        var (tablaElegida, fila) = ElegirCandidato(candidatos, etiqueta.Arribo);
        sku = ObtenerValor(fila, "SKU");
        lote = ObtenerValor(fila, "LOTE");
        cajas = ObtenerValor(fila, "CAJAS");
        var cantidadTexto = ObtenerValor(fila, "CANTIDAD");
        if (decimal.TryParse(cantidadTexto, NumberStyles.Number, CultureInfo.InvariantCulture, out var c))
        {
            cantidad = c;
        }

        cliente = ResolverCliente(tablaElegida, etiqueta.Arribo);
        variablesJson = JsonSerializer.Serialize(fila);
        conMetadatos++;
    }
    else
    {
        cliente = ResolverClienteDesdeArribo(etiqueta.Arribo);
        variablesJson = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["LPN"] = etiqueta.Lpn,
            ["ARRIBO"] = etiqueta.Arribo,
            ["SOLICITANTE"] = etiqueta.Solicitante,
        });
        sinMetadatos++;
    }

    if (aliasCliente.TryGetValue(cliente, out var clienteReal))
    {
        cliente = clienteReal;
    }

    registros.Add(new HistoricoImpresionRecord(
        Lpn: etiqueta.Lpn,
        Consecutivo: consecutivoSeguro,
        Tipo: tipoNormalizado,
        Cliente: cliente,
        Solicitante: string.IsNullOrWhiteSpace(etiqueta.Solicitante) ? "DESCONOCIDO" : etiqueta.Solicitante,
        Arribo: string.IsNullOrWhiteSpace(etiqueta.Arribo) ? "DESCONOCIDO" : etiqueta.Arribo,
        FechaHora: fechaUtc,
        Sku: sku,
        Lote: lote,
        Cantidad: cantidad,
        Cajas: cajas,
        TarimaActual: null,
        TarimaTotal: null,
        VariablesJson: variablesJson));
}

Console.WriteLine($"Registros preparados: {registros.Count} (con metadatos de respaldo: {conMetadatos}, sin match: {sinMetadatos})");

// ---------------------------------------------------------------------------------
// 4) Insertar en Neon, en lotes, vía NpgsqlBatch, con ON CONFLICT (lpn) DO NOTHING.
// ---------------------------------------------------------------------------------
var connectionString = NeonConnectionStringResolver.Resolve(raizRepo);
await using var dataSource = NeonDataSourceFactory.Create(connectionString);

var insertados = 0;
const int tamanoLote = 500;

await using (var connection = await dataSource.OpenConnectionAsync())
await using (var transaction = await connection.BeginTransactionAsync())
{
    foreach (var grupo in registros.Chunk(tamanoLote))
    {
        await using var batch = new NpgsqlBatch(connection, transaction);
        foreach (var r in grupo)
        {
            var comando = new NpgsqlBatchCommand(
                """
                INSERT INTO historico_impresiones (lpn, consecutivo, tipo, cliente, solicitante, arribo, fecha_hora, sku, lote, cantidad, variables_json)
                VALUES (@lpn, @consecutivo, @tipo, @cliente, @solicitante, @arribo, @fecha_hora, @sku, @lote, @cantidad, @variables_json)
                ON CONFLICT (lpn) DO NOTHING
                RETURNING lpn;
                """);
            comando.Parameters.AddWithValue("lpn", r.Lpn);
            comando.Parameters.AddWithValue("consecutivo", (object?)r.Consecutivo ?? DBNull.Value);
            comando.Parameters.AddWithValue("tipo", (object?)r.Tipo ?? DBNull.Value);
            comando.Parameters.AddWithValue("cliente", (object?)r.Cliente ?? DBNull.Value);
            comando.Parameters.AddWithValue("solicitante", r.Solicitante);
            comando.Parameters.AddWithValue("arribo", r.Arribo);
            comando.Parameters.AddWithValue("fecha_hora", r.FechaHora.ToUniversalTime());
            comando.Parameters.AddWithValue("sku", (object?)r.Sku ?? DBNull.Value);
            comando.Parameters.AddWithValue("lote", (object?)r.Lote ?? DBNull.Value);
            comando.Parameters.AddWithValue("cantidad", (object?)r.Cantidad ?? DBNull.Value);
            comando.Parameters.Add(new NpgsqlParameter("variables_json", NpgsqlDbType.Jsonb)
            {
                Value = (object?)r.VariablesJson ?? DBNull.Value
            });
            batch.BatchCommands.Add(comando);
        }

        await using var reader = await batch.ExecuteReaderAsync();
        do
        {
            while (await reader.ReadAsync())
            {
                insertados++; // cada fila devuelta por RETURNING = una inserción real (no fue conflicto de LPN)
            }
        }
        while (await reader.NextResultAsync());
    }

    await transaction.CommitAsync();
}

Console.WriteLine($"Filas realmente insertadas en Neon: {insertados} (de {registros.Count} intentadas; el resto ya existía por LPN — ON CONFLICT DO NOTHING).");

// ---------------------------------------------------------------------------------
// 5) Recalcular MAX(consecutivo) por tipo directamente en Neon (post-carga) y ajustar
//    secuencias_lpn con GREATEST(), para nunca retroceder un valor ya existente.
// ---------------------------------------------------------------------------------
var anioActual = DateTime.UtcNow.Year;
var mapaClaves = new (string Tipo, string Clave, int? Anio)[]
{
    ("Normal", $"NORMAL_{anioActual}", anioActual),
    ("Pnc", $"PNC_{anioActual}", anioActual),
    ("MnsPncD", "MNS_PNC_D", null),
    ("MnsPncM", "MNS_PNC_M", null),
};

await using (var connection = await dataSource.OpenConnectionAsync())
{
    Console.WriteLine("--- secuencias_lpn ajustadas ---");
    foreach (var (tipo, clave, anio) in mapaClaves)
    {
        int? maximo;
        await using (var cmdMax = new NpgsqlCommand("SELECT MAX(consecutivo) FROM historico_impresiones WHERE tipo = @tipo;", connection))
        {
            cmdMax.Parameters.AddWithValue("tipo", tipo);
            var resultado = await cmdMax.ExecuteScalarAsync();
            maximo = resultado is null or DBNull ? null : Convert.ToInt32(resultado);
        }

        if (maximo is null)
        {
            Console.WriteLine($"  {clave}: sin filas de tipo '{tipo}' en historico_impresiones; no se modifica.");
            continue;
        }

        await using (var cmdUpsert = new NpgsqlCommand(
            """
            INSERT INTO secuencias_lpn (clave, tipo, anio, ultimo_consecutivo)
            VALUES (@clave, @tipoSecuencia, @anio, @maximo)
            ON CONFLICT (clave) DO UPDATE SET
                ultimo_consecutivo = GREATEST(secuencias_lpn.ultimo_consecutivo, EXCLUDED.ultimo_consecutivo),
                actualizado_en = now();
            """, connection))
        {
            cmdUpsert.Parameters.AddWithValue("clave", clave);
            cmdUpsert.Parameters.AddWithValue("tipoSecuencia", tipo == "Normal" ? "NORMAL" : tipo == "Pnc" ? "PNC" : tipo == "MnsPncD" ? "MNS_PNC_D" : "MNS_PNC_M");
            cmdUpsert.Parameters.AddWithValue("anio", (object?)anio ?? DBNull.Value);
            cmdUpsert.Parameters.AddWithValue("maximo", maximo.Value);
            await cmdUpsert.ExecuteNonQueryAsync();
        }

        await using (var cmdFinal = new NpgsqlCommand("SELECT ultimo_consecutivo FROM secuencias_lpn WHERE clave = @clave;", connection))
        {
            cmdFinal.Parameters.AddWithValue("clave", clave);
            var valorFinal = await cmdFinal.ExecuteScalarAsync();
            Console.WriteLine($"  {clave}: MAX(consecutivo) migrado = {maximo}; valor final en secuencias_lpn = {valorFinal}");
        }
    }
}

Console.WriteLine("MIGRACION_COMPLETA");
return 0;

// ===================================================================================

static string NormalizarTipo(string tipoLegado) => tipoLegado.Trim().ToUpperInvariant() switch
{
    "NORMAL" => "Normal",
    "PNC" => "Pnc",
    "MNS_PNC_D" => "MnsPncD",
    "MNS_PNC_M" => "MnsPncM",
    _ => tipoLegado, // p. ej. MANUAL_EXTERNO: no tiene equivalente nuevo, se preserva tal cual.
};

static DateTimeOffset ConvertirAUtc(string fechaHoraTexto, TimeSpan offsetPiso)
{
    if (!DateTime.TryParse(fechaHoraTexto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
    {
        return DateTimeOffset.UtcNow;
    }

    return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offsetPiso).ToUniversalTime();
}

static string? ObtenerValor(Dictionary<string, string?> fila, string columna) =>
    fila.TryGetValue(columna.ToUpperInvariant(), out var valor) ? valor : null;

static (string Tabla, Dictionary<string, string?> Fila) ElegirCandidato(
    List<(string Tabla, Dictionary<string, string?> Fila)> candidatos, string arriboEtiqueta)
{
    if (candidatos.Count == 1)
    {
        return candidatos[0];
    }

    // Colisión entre tablas (misma LPN respaldada bajo más de un cliente): se desempata
    // comparando el ARRIBO propio de cada candidato contra el arribo real de la etiqueta.
    var conArriboCoincidente = candidatos.FirstOrDefault(c =>
        ObtenerValor(c.Fila, "ARRIBO") is { } a && string.Equals(a.Trim(), arriboEtiqueta.Trim(), StringComparison.OrdinalIgnoreCase));

    return conArriboCoincidente.Fila is not null ? conArriboCoincidente : candidatos[0];
}

static string ResolverCliente(string tablaOrigen, string arribo)
{
    var tablaUpper = tablaOrigen.Trim().ToUpperInvariant();
    // Nombres de tabla "limpios" (códigos de cliente reales) se usan tal cual; nombres
    // de tabla "sucios" (p. ej. creados por error con el nombre de un arribo) se
    // resuelven por el prefijo del arribo en su lugar.
    var pareceCodigoLimpio = tablaUpper.Length <= 15 && !tablaUpper.Contains(' ') && !tablaUpper.Any(char.IsDigit);
    return pareceCodigoLimpio ? tablaUpper : ResolverClienteDesdeArribo(arribo);
}

static string ResolverClienteDesdeArribo(string arribo)
{
    var texto = (arribo ?? string.Empty).Trim();
    if (texto.Length == 0)
    {
        return "DESCONOCIDO";
    }

    var prefijo = texto.Split('-', 2)[0].Trim().ToUpperInvariant();
    return prefijo.Length > 0 ? prefijo : "DESCONOCIDO";
}

internal sealed record Etiqueta(string Lpn, long Consecutivo, string Tipo, string Solicitante, string Arribo, string FechaHoraTexto);
