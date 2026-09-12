using Microsoft.Data.Sqlite;
using Zps.Data.LocalCache;
using Zps.Data.Models;

namespace Zps.Data.IntegrationTests;

/// <summary>
/// Ejercita la caché local SQLite real (archivo en disco temporal, no en memoria) para
/// confirmar que el modo WAL se activa y que el ciclo réplica-lee funciona tanto para
/// cat_clientes como para historico_impresiones.
/// </summary>
public sealed class LocalCacheStoreTests : IDisposable
{
    private readonly string _rutaTemporal;

    public LocalCacheStoreTests()
    {
        _rutaTemporal = Path.Combine(Path.GetTempPath(), $"zps_cache_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var archivo in new[] { _rutaTemporal, _rutaTemporal + "-wal", _rutaTemporal + "-shm" })
        {
            if (File.Exists(archivo))
            {
                File.Delete(archivo);
            }
        }
    }

    [Fact]
    public async Task InicializarAsync_HabilitaModoWal()
    {
        var store = new LocalCacheStore(_rutaTemporal);
        await store.InicializarAsync();

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _rutaTemporal }.ToString());
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var modo = (string)(await cmd.ExecuteScalarAsync())!;

        Assert.Equal("wal", modo, ignoreCase: true);
    }

    [Fact]
    public async Task ReplicarClientesAsync_LuegoObtenerClientesAsync_DevuelveLoReplicado()
    {
        var store = new LocalCacheStore(_rutaTemporal);
        await store.InicializarAsync();

        var cliente = new ClienteCatalogoRecord(
            Codigo: "MINISO",
            Nombre: "Miniso",
            Activo: true,
            PrefijoFolio: null,
            EstrategiaLpn: "MINISO",
            PlantillaZpl: null,
            MapeoColumnas: new Dictionary<string, string>(),
            ActualizadoEn: DateTimeOffset.UtcNow);

        await store.ReplicarClientesAsync(new[] { cliente });
        var clientes = await store.ObtenerClientesAsync();

        var recuperado = Assert.Single(clientes);
        Assert.Equal("MINISO", recuperado.Codigo);
        Assert.Equal("MINISO", recuperado.EstrategiaLpn);
        Assert.True(recuperado.Activo);
    }

    [Fact]
    public async Task ReplicarHistoricoAsync_LuegoObtenerHistoricoPorArriboAsync_DevuelveLoReplicado()
    {
        var store = new LocalCacheStore(_rutaTemporal);
        await store.InicializarAsync();

        var registro = new HistoricoImpresionRecord(
            Lpn: "LGMA20260000001",
            Consecutivo: 1,
            Tipo: "NORMAL",
            Cliente: "MYC",
            Solicitante: "PRUEBA",
            Arribo: "ARRIBO-TEST-1",
            FechaHora: DateTimeOffset.UtcNow,
            Sku: "ABC123",
            Lote: "LOTE-9",
            Cantidad: 36.50m,
            Cajas: "NV",
            TarimaActual: 1,
            TarimaTotal: 1,
            VariablesJson: "{\"SKU\":\"ABC123\",\"LOTE\":\"LOTE-9\",\"CANTIDAD\":\"36.50\",\"ATRIBUTO\":\"X\"}");

        await store.ReplicarHistoricoAsync(new[] { registro });
        var historico = await store.ObtenerHistoricoPorArriboAsync("MYC", "ARRIBO-TEST-1");

        var recuperado = Assert.Single(historico);
        Assert.Equal("LGMA20260000001", recuperado.Lpn);
        Assert.Equal(1, recuperado.Consecutivo);
        Assert.Equal("ABC123", recuperado.Sku);
        Assert.Equal("LOTE-9", recuperado.Lote);
        Assert.Equal(36.50m, recuperado.Cantidad);
        Assert.Equal(registro.VariablesJson, recuperado.VariablesJson);
    }

    [Fact]
    public async Task BuscarHistoricoAsync_FiltraPorSkuYLote()
    {
        var store = new LocalCacheStore(_rutaTemporal);
        await store.InicializarAsync();

        var registros = new[]
        {
            new HistoricoImpresionRecord(
                Lpn: "LGMA20260000010", Consecutivo: 10, Tipo: "NORMAL", Cliente: "MYC",
                Solicitante: "PRUEBA", Arribo: "ARRIBO-A", FechaHora: DateTimeOffset.UtcNow,
                Sku: "SKU-BUSCADO", Lote: "L1", Cantidad: 5m, Cajas: null, TarimaActual: null, TarimaTotal: null, VariablesJson: null),
            new HistoricoImpresionRecord(
                Lpn: "LGMA20260000011", Consecutivo: 11, Tipo: "NORMAL", Cliente: "MYC",
                Solicitante: "PRUEBA", Arribo: "ARRIBO-B", FechaHora: DateTimeOffset.UtcNow,
                Sku: "OTRO-SKU", Lote: "LOTE-BUSCADO", Cantidad: 7m, Cajas: null, TarimaActual: null, TarimaTotal: null, VariablesJson: null),
            new HistoricoImpresionRecord(
                Lpn: "LGMA20260000012", Consecutivo: 12, Tipo: "NORMAL", Cliente: "MYC",
                Solicitante: "PRUEBA", Arribo: "ARRIBO-C", FechaHora: DateTimeOffset.UtcNow,
                Sku: "NINGUNO", Lote: "NADA", Cantidad: 1m, Cajas: null, TarimaActual: null, TarimaTotal: null, VariablesJson: null),
        };

        await store.ReplicarHistoricoAsync(registros);

        var porSku = await store.BuscarHistoricoAsync("SKU-BUSCADO");
        Assert.Equal("LGMA20260000010", Assert.Single(porSku).Lpn);

        var porLote = await store.BuscarHistoricoAsync("LOTE-BUSCADO");
        Assert.Equal("LGMA20260000011", Assert.Single(porLote).Lpn);
    }
}
