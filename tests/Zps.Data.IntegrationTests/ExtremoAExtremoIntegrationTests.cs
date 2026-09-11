using Microsoft.Data.Sqlite;
using Npgsql;
using Zps.Data.LocalCache;
using Zps.Data.Models;

namespace Zps.Data.IntegrationTests;

/// <summary>
/// Prueba de extremo a extremo, contra infraestructura real (sin mocks):
///   1) Reserva un lote de folios de forma atómica y concurrente contra Neon
///      (ConsecutivosService, SELECT ... FOR UPDATE sobre secuencias_lpn).
///   2) Persiste esos folios en Neon (HistoricoService -> historico_impresiones).
///   3) Replica ese mismo lote hacia la caché local SQLite (LocalCacheStore, modo WAL).
///   4) Confirma que lo leído desde el SQLite local coincide exactamente con lo
///      reservado/persistido en Neon, validando que la reimpresión offline funcionaría
///      con datos íntegros aunque Neon deje de estar disponible.
///
/// Usa una clave de secuencia y un arribo sintéticos y únicos por ejecución, y limpia
/// todo lo que crea en Neon al finalizar (secuencias_lpn e historico_impresiones).
/// </summary>
public sealed class ExtremoAExtremoIntegrationTests : IAsyncLifetime
{
    private NpgsqlDataSource _dataSource = null!;
    private string _rutaCacheLocal = null!;

    public Task InitializeAsync()
    {
        _dataSource = NeonDataSourceFactory.Create(NeonTestConnection.GetConnectionString());
        _rutaCacheLocal = Path.Combine(Path.GetTempPath(), $"zps_cache_e2e_{Guid.NewGuid():N}.db");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();

        SqliteConnection.ClearAllPools();
        foreach (var archivo in new[] { _rutaCacheLocal, _rutaCacheLocal + "-wal", _rutaCacheLocal + "-shm" })
        {
            if (File.Exists(archivo))
            {
                File.Delete(archivo);
            }
        }
    }

    [Fact]
    public async Task ReservaAtomicaConcurrente_PersisteEnNeon_YSeReplicaCorrectamenteAlCacheLocal()
    {
        var clave = $"TEST_E2E_{Guid.NewGuid():N}";
        var cliente = "TEST_CLIENTE_E2E";
        var arribo = $"ARRIBO-E2E-{Guid.NewGuid():N}";

        var consecutivos = new ConsecutivosService(_dataSource);
        var historico = new HistoricoService(_dataSource);

        try
        {
            // --- 1) Reserva atómica concurrente contra Neon: 8 hilos piden 5 folios cada uno. ---
            const int lotesConcurrentes = 8;
            const int cantidadPorLote = 5;

            var tareasReserva = Enumerable.Range(0, lotesConcurrentes)
                .Select(_ => consecutivos.ReservarLoteAsync(clave, "TEST", cantidadPorLote))
                .ToArray();
            var bloques = await Task.WhenAll(tareasReserva);
            var foliosReservados = bloques.SelectMany(b => b).OrderBy(n => n).ToArray();

            var esperados = lotesConcurrentes * cantidadPorLote;
            Assert.Equal(esperados, foliosReservados.Length);
            Assert.Equal(esperados, foliosReservados.Distinct().Count()); // sin colisiones
            Assert.Equal(Enumerable.Range(1, esperados), foliosReservados); // sin huecos, bloque 1..N

            // --- 2) Persiste el lote reservado en historico_impresiones (Neon), en batch. ---
            var registros = foliosReservados
                .Select(n => new HistoricoImpresionRecord(
                    Lpn: $"TESTLPN{n:D5}",
                    Consecutivo: n,
                    Tipo: "TEST",
                    Cliente: cliente,
                    Solicitante: "INTEGRACION",
                    Arribo: arribo,
                    FechaHora: DateTimeOffset.UtcNow,
                    Sku: $"SKU-{n:D5}",
                    Lote: "LOTE-E2E",
                    Cantidad: n,
                    Cajas: "CAJA-E2E",
                    VariablesJson: $$"""{"consecutivo": {{n}} }"""))
                .ToList();

            await historico.InsertarLoteAsync(registros);

            var persistidoEnNeon = await LeerHistoricoDeNeonAsync(arribo);
            Assert.Equal(esperados, persistidoEnNeon.Count);
            Assert.Equal(registros.Select(r => r.Lpn).OrderBy(x => x), persistidoEnNeon.OrderBy(x => x));

            // --- 3) Replica ese mismo lote a la caché local SQLite (WAL). ---
            var cache = new LocalCacheStore(_rutaCacheLocal);
            await cache.InicializarAsync();
            await cache.ReplicarHistoricoAsync(registros);

            // --- 4) Confirma que la lectura offline (SQLite local) es íntegra respecto a Neon. ---
            var desdeCacheLocal = await cache.ObtenerHistoricoPorArriboAsync(cliente, arribo);

            Assert.Equal(esperados, desdeCacheLocal.Count);
            Assert.Equal(
                registros.Select(r => r.Lpn).OrderBy(x => x),
                desdeCacheLocal.Select(r => r.Lpn).OrderBy(x => x));
            Assert.Equal(
                registros.Select(r => r.Consecutivo).OrderBy(x => x),
                desdeCacheLocal.Select(r => r.Consecutivo).OrderBy(x => x));
            Assert.Equal(
                registros.Select(r => r.Sku).OrderBy(x => x),
                desdeCacheLocal.Select(r => r.Sku).OrderBy(x => x));
            Assert.Equal(
                registros.Select(r => r.Cantidad).OrderBy(x => x),
                desdeCacheLocal.Select(r => r.Cantidad).OrderBy(x => x));
        }
        finally
        {
            await LimpiarNeonAsync(clave, arribo);
        }
    }

    private async Task<List<string>> LeerHistoricoDeNeonAsync(string arribo)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT lpn FROM historico_impresiones WHERE arribo = @arribo;", connection);
        cmd.Parameters.AddWithValue("arribo", arribo);

        var resultado = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            resultado.Add(reader.GetString(0));
        }

        return resultado;
    }

    private async Task LimpiarNeonAsync(string clave, string arribo)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();

        await using (var borrarHistorico = new NpgsqlCommand("DELETE FROM historico_impresiones WHERE arribo = @arribo;", connection))
        {
            borrarHistorico.Parameters.AddWithValue("arribo", arribo);
            await borrarHistorico.ExecuteNonQueryAsync();
        }

        await using (var borrarSecuencia = new NpgsqlCommand("DELETE FROM secuencias_lpn WHERE clave = @clave;", connection))
        {
            borrarSecuencia.Parameters.AddWithValue("clave", clave);
            await borrarSecuencia.ExecuteNonQueryAsync();
        }
    }
}
