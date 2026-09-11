using Microsoft.Data.Sqlite;
using Npgsql;
using Zps.Data.LocalCache;
using Zps.Data.Models;

namespace Zps.Data.IntegrationTests;

/// <summary>
/// Prueba real (sin mocks) del borrado seguro desde Histórico: inserta registros
/// sintéticos en Neon y en la caché local, los elimina vía HistoricoService.EliminarAsync
/// / LocalCacheStore.EliminarHistoricoAsync, y confirma que desaparecen de ambos lados.
/// </summary>
public sealed class HistoricoEliminarIntegrationTests : IAsyncLifetime
{
    private NpgsqlDataSource _dataSource = null!;
    private string _rutaCacheLocal = null!;

    public Task InitializeAsync()
    {
        _dataSource = NeonDataSourceFactory.Create(NeonTestConnection.GetConnectionString());
        _rutaCacheLocal = Path.Combine(Path.GetTempPath(), $"zps_cache_eliminar_{Guid.NewGuid():N}.db");
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
    public async Task EliminarAsync_BorraDeNeonYDeLaCacheLocal_YPreservaLosNoSeleccionados()
    {
        var historico = new HistoricoService(_dataSource);
        var cache = new LocalCacheStore(_rutaCacheLocal);
        await cache.InicializarAsync();

        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var registros = new[]
        {
            NuevoRegistro($"TEST-BORRAR-{sufijo}-1"),
            NuevoRegistro($"TEST-BORRAR-{sufijo}-2"),
            NuevoRegistro($"TEST-CONSERVAR-{sufijo}"),
        };

        await historico.InsertarLoteAsync(registros);
        await cache.ReplicarHistoricoAsync(registros);

        try
        {
            var aEliminar = new[] { registros[0].Lpn, registros[1].Lpn };

            var afectadasEnNeon = await historico.EliminarAsync(aEliminar);
            await cache.EliminarHistoricoAsync(aEliminar);

            Assert.Equal(2, afectadasEnNeon);

            // Los eliminados ya no aparecen en Neon...
            var resultadoNeon = await historico.BuscarAsync(sufijo);
            Assert.DoesNotContain(resultadoNeon, r => r.Lpn == registros[0].Lpn || r.Lpn == registros[1].Lpn);
            // ...pero el que no se pidió eliminar sigue intacto.
            Assert.Contains(resultadoNeon, r => r.Lpn == registros[2].Lpn);

            // Mismo comportamiento en la caché local.
            var resultadoLocal = await cache.BuscarHistoricoAsync(sufijo);
            Assert.DoesNotContain(resultadoLocal, r => r.Lpn == registros[0].Lpn || r.Lpn == registros[1].Lpn);
            Assert.Contains(resultadoLocal, r => r.Lpn == registros[2].Lpn);
        }
        finally
        {
            // Limpieza total: incluye el registro "a conservar" para no ensuciar Neon.
            await historico.EliminarAsync(registros.Select(r => r.Lpn).ToList());
        }
    }

    [Fact]
    public async Task EliminarAsync_ListaVacia_NoHaceNadaYNoLanza()
    {
        var historico = new HistoricoService(_dataSource);

        var afectadas = await historico.EliminarAsync(Array.Empty<string>());

        Assert.Equal(0, afectadas);
    }

    private static HistoricoImpresionRecord NuevoRegistro(string lpn) => new(
        Lpn: lpn,
        Consecutivo: 1,
        Tipo: "TEST",
        Cliente: "TEST_CLIENTE",
        Solicitante: "INTEGRACION",
        Arribo: "ARRIBO-ELIMINAR-TEST",
        FechaHora: DateTimeOffset.UtcNow,
        Sku: "SKU-TEST",
        Lote: "LOTE-TEST",
        Cantidad: 1m,
        VariablesJson: "{}");
}
