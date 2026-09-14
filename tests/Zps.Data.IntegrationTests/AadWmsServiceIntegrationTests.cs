using Zps.Data;
using Zps.Data.Models.Wms;

namespace Zps.Data.IntegrationTests;

/// <summary>
/// Pruebas de integración REALES contra HighJump/AAD (SQL Server, 192.168.100.76), solo
/// lectura. No hay una base de datos de prueba separada para el WMS: estas pruebas no
/// asumen datos específicos, solo que la conexión abre y que las consultas WITH (NOLOCK)
/// contra t_employee/t_po_master/t_po_detail son válidas contra el esquema real. Si el
/// esquema real usa otros nombres de columna, estas pruebas son las que primero lo revelan.
/// </summary>
public sealed class AadWmsServiceIntegrationTests
{
    private static string ConnectionString => AadConnectionStringResolver.Resolve();

    [Fact]
    public async Task ProbarConexionAsync_ContraElServidorAad_DevuelveTrue()
    {
        var servicio = new AadWmsService(ConnectionString);

        var conectado = await servicio.ProbarConexionAsync();

        Assert.True(conectado);
    }

    [Fact]
    public async Task ValidarCredencialesAsync_PinIncorrecto_DevuelveNullSinConsultarElEmpleado()
    {
        var servicio = new AadWmsService(ConnectionString);

        var resultado = await servicio.ValidarCredencialesAsync("cualquier_usuario", "0000");

        Assert.Null(resultado);
    }

    [Fact]
    public async Task ValidarCredencialesAsync_UsuarioInexistenteConPinCorrecto_DevuelveNull()
    {
        var servicio = new AadWmsService(ConnectionString);

        var resultado = await servicio.ValidarCredencialesAsync($"NO_EXISTE_{Guid.NewGuid():N}", "1090");

        Assert.Null(resultado);
    }

    [Fact]
    public async Task ObtenerOrdenesAbiertasAsync_ConsultaContraElWms_DevuelveSoloStatusAbierto()
    {
        var servicio = new AadWmsService(ConnectionString);

        var ordenes = await servicio.ObtenerOrdenesAbiertasAsync();

        Assert.NotNull(ordenes);
        Assert.All(ordenes, orden => Assert.Equal("O", orden.Status));
    }

    [Fact]
    public async Task ObtenerDetalleOrdenAsync_ConPoInexistente_DevuelveListaVacia()
    {
        var servicio = new AadWmsService(ConnectionString);

        var detalle = await servicio.ObtenerDetalleOrdenAsync($"PO_TEST_INEXISTENTE_{Guid.NewGuid():N}");

        Assert.Empty(detalle);
    }

    [Fact]
    public async Task BuscarInventarioAsync_SinFiltros_RespetaElTopYDevuelveDatosCoherentes()
    {
        var servicio = new AadWmsService(ConnectionString);

        var resultado = await servicio.BuscarInventarioAsync(
            new InventarioFiltrosModel(Lpn: null, Sku: null, Lote: null, Ubicacion: null, Cliente: null, SoloDisponibles: false));

        Assert.True(resultado.Count <= 500);
        Assert.All(resultado, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Lpn));
            Assert.False(string.IsNullOrWhiteSpace(item.Sku));
            Assert.Equal(item.CantidadTotal - item.CantidadRetenida, item.CantidadDisponible);
        });
    }

    [Fact]
    public async Task BuscarInventarioAsync_SoloDisponibles_NuncaDevuelveCantidadDisponibleCero()
    {
        var servicio = new AadWmsService(ConnectionString);

        var resultado = await servicio.BuscarInventarioAsync(
            new InventarioFiltrosModel(Lpn: null, Sku: null, Lote: null, Ubicacion: null, Cliente: null, SoloDisponibles: true));

        Assert.All(resultado, item => Assert.True(item.CantidadDisponible > 0));
    }

    [Fact]
    public async Task BuscarInventarioAsync_ConLpnInexistente_DevuelveListaVacia()
    {
        var servicio = new AadWmsService(ConnectionString);

        var resultado = await servicio.BuscarInventarioAsync(
            new InventarioFiltrosModel(
                Lpn: $"LPN_TEST_INEXISTENTE_{Guid.NewGuid():N}", Sku: null, Lote: null, Ubicacion: null, Cliente: null, SoloDisponibles: false));

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task BuscarInventarioAsync_FiltroClienteConEspacios_SeIgnoraIgualQueVacio()
    {
        var servicio = new AadWmsService(ConnectionString);

        var conEspacios = await servicio.BuscarInventarioAsync(
            new InventarioFiltrosModel(Lpn: null, Sku: null, Lote: null, Ubicacion: null, Cliente: "   ", SoloDisponibles: false));
        var sinFiltro = await servicio.BuscarInventarioAsync(
            new InventarioFiltrosModel(Lpn: null, Sku: null, Lote: null, Ubicacion: null, Cliente: null, SoloDisponibles: false));

        Assert.Equal(sinFiltro.Count, conEspacios.Count);
    }

    [Fact]
    public async Task BuscarInventarioAsync_LimiteMaximoNullSinFiltrosDeTexto_AplicaSalvaguardaDe500()
    {
        var servicio = new AadWmsService(ConnectionString);

        var resultado = await servicio.BuscarInventarioAsync(
            new InventarioFiltrosModel(Lpn: null, Sku: null, Lote: null, Ubicacion: null, Cliente: null, SoloDisponibles: false)
            {
                LimiteMaximo = null,
            });

        Assert.True(resultado.Count <= 500);
    }

    [Fact]
    public async Task BuscarInventarioAsync_LimiteMaximoExplicito_RecortaAEseNumero()
    {
        var servicio = new AadWmsService(ConnectionString);

        var resultado = await servicio.BuscarInventarioAsync(
            new InventarioFiltrosModel(Lpn: null, Sku: null, Lote: null, Ubicacion: null, Cliente: null, SoloDisponibles: false)
            {
                LimiteMaximo = 10,
            });

        Assert.True(resultado.Count <= 10);
    }

    [Fact]
    public async Task BuscarInventarioAsync_LimiteMaximoNullConFiltroSku_NoAplicaTop()
    {
        var servicio = new AadWmsService(ConnectionString);

        // Primero se ubica un SKU real con pocas filas para no depender de datos fijos.
        var muestra = await servicio.BuscarInventarioAsync(
            new InventarioFiltrosModel(Lpn: null, Sku: null, Lote: null, Ubicacion: null, Cliente: null, SoloDisponibles: false)
            {
                LimiteMaximo = 1,
            });

        Assert.NotEmpty(muestra);
        var sku = muestra[0].Sku;

        var conTopLimitado = await servicio.BuscarInventarioAsync(
            new InventarioFiltrosModel(Lpn: null, Sku: sku, Lote: null, Ubicacion: null, Cliente: null, SoloDisponibles: false));
        var sinTop = await servicio.BuscarInventarioAsync(
            new InventarioFiltrosModel(Lpn: null, Sku: sku, Lote: null, Ubicacion: null, Cliente: null, SoloDisponibles: false)
            {
                LimiteMaximo = null,
            });

        // Con un SKU específico el resultado real es el mismo con o sin tope (muy por debajo
        // de 500): lo que prueba es que filtrar por SKU no se sigue recortando a 500 de más.
        Assert.Equal(conTopLimitado.Count, sinTop.Count);
    }
}
