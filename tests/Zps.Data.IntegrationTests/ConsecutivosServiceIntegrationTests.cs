using Npgsql;
using Zps.Data;

namespace Zps.Data.IntegrationTests;

/// <summary>
/// Prueba de integración REAL contra Neon (no usa mocks ni una base de datos en memoria):
/// valida que ConsecutivosService.ReservarLoteAsync reserva folios de forma atómica y
/// contigua bajo concurrencia real, gracias al SELECT ... FOR UPDATE sobre secuencias_lpn.
///
/// Cada prueba usa una clave de secuencia sintética y única (TEST_INTEGRACION_&lt;guid&gt;)
/// para no tocar los contadores reales de producción (NORMAL_2026, PNC_2026, etc.), y la
/// elimina de secuencias_lpn al finalizar.
/// </summary>
public sealed class ConsecutivosServiceIntegrationTests : IAsyncLifetime
{
    private NpgsqlDataSource _dataSource = null!;

    public Task InitializeAsync()
    {
        _dataSource = NeonDataSourceFactory.Create(NeonTestConnection.GetConnectionString());
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
    }

    [Fact]
    public async Task ReservarLoteAsync_LlamadasSecuenciales_DevuelveBloquesContiguosSinHuecos()
    {
        var clave = $"TEST_INTEGRACION_{Guid.NewGuid():N}";
        var servicio = new ConsecutivosService(_dataSource);

        try
        {
            var primerLote = await servicio.ReservarLoteAsync(clave, "TEST", cantidad: 5);
            var segundoLote = await servicio.ReservarLoteAsync(clave, "TEST", cantidad: 3);

            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, primerLote);
            Assert.Equal(new[] { 6, 7, 8 }, segundoLote);
        }
        finally
        {
            await LimpiarClaveAsync(clave);
        }
    }

    [Fact]
    public async Task ReservarLoteAsync_ReservasConcurrentes_SonAtomicasSinHuecosNiColisiones()
    {
        var clave = $"TEST_INTEGRACION_{Guid.NewGuid():N}";
        var servicio = new ConsecutivosService(_dataSource);
        const int lotesConcurrentes = 20;
        const int cantidadPorLote = 7;

        try
        {
            var tareas = Enumerable.Range(0, lotesConcurrentes)
                .Select(_ => servicio.ReservarLoteAsync(clave, "TEST", cantidadPorLote))
                .ToArray();

            var resultados = await Task.WhenAll(tareas);
            var todosLosFolios = resultados.SelectMany(bloque => bloque).ToArray();
            var esperados = lotesConcurrentes * cantidadPorLote;

            // Sin colisiones: cada número reservado por cualquier hilo es único.
            Assert.Equal(esperados, todosLosFolios.Length);
            Assert.Equal(esperados, todosLosFolios.Distinct().Count());

            // Sin huecos: el conjunto reservado es exactamente el rango contiguo 1..N,
            // sin importar el orden real de ejecución de los 20 lotes concurrentes.
            var ordenados = todosLosFolios.OrderBy(n => n).ToArray();
            for (var i = 0; i < ordenados.Length; i++)
            {
                Assert.Equal(i + 1, ordenados[i]);
            }
        }
        finally
        {
            await LimpiarClaveAsync(clave);
        }
    }

    private async Task LimpiarClaveAsync(string clave)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM secuencias_lpn WHERE clave = @clave;", connection);
        cmd.Parameters.AddWithValue("clave", clave);
        await cmd.ExecuteNonQueryAsync();
    }
}
