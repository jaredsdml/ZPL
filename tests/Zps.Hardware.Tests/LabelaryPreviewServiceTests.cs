using System.Net;
using Zps.Hardware;

namespace Zps.Hardware.Tests;

/// <summary>
/// Usa un HttpMessageHandler falso: ninguna de estas pruebas toca la red real ni depende
/// de que api.labelary.com esté disponible.
/// </summary>
public class LabelaryPreviewServiceTests
{
    private static readonly byte[] PngFalso = { 0x89, 0x50, 0x4E, 0x47 }; // firma PNG

    [Fact]
    public async Task GenerarVistaPreviaAsync_RespuestaExitosa_DevuelveLosBytesDeLaImagen()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(PngFalso)
            }));
        using var httpClient = new HttpClient(handler);
        using var servicio = new LabelaryPreviewService(httpClient);

        var resultado = await servicio.GenerarVistaPreviaAsync("^XA^XZ");

        Assert.True(resultado.Exito);
        Assert.Null(resultado.Error);
        Assert.Equal(PngFalso, resultado.ImagenPng);
        Assert.Contains("^XA^XZ", handler.UltimoContenidoEnviado);
    }

    [Fact]
    public async Task GenerarVistaPreviaAsync_LabelaryRespondeError_DevuelveFalloConElCodigoDeEstado()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                ReasonPhrase = "Bad ZPL",
                Content = new StringContent("ZPL inválido")
            }));
        using var httpClient = new HttpClient(handler);
        using var servicio = new LabelaryPreviewService(httpClient);

        var resultado = await servicio.GenerarVistaPreviaAsync("^XA^XZ");

        Assert.False(resultado.Exito);
        Assert.Null(resultado.ImagenPng);
        Assert.Contains("400", resultado.Error);
    }

    [Fact]
    public async Task GenerarVistaPreviaAsync_ExcedeElTimeout_DevuelveFalloSinLanzarExcepcion()
    {
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            // Simula un servidor colgado: espera mucho más que el timeout configurado.
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        using var servicio = new LabelaryPreviewService(httpClient, timeout: TimeSpan.FromMilliseconds(150));

        var resultado = await servicio.GenerarVistaPreviaAsync("^XA^XZ");

        Assert.False(resultado.Exito);
        Assert.Contains("Tiempo de espera agotado", resultado.Error);
    }

    [Fact]
    public async Task GenerarVistaPreviaAsync_TimeoutSolicitadoMayorAlMaximo_SeAcotaAlMaximoPermitido()
    {
        // El motor exige un tope estricto de 3-4s; pedir 30s no debe respetarse literalmente.
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        using var servicio = new LabelaryPreviewService(httpClient, timeout: TimeSpan.FromSeconds(30));

        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        var resultado = await servicio.GenerarVistaPreviaAsync("^XA^XZ");
        cronometro.Stop();

        Assert.False(resultado.Exito);
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(5),
            $"El timeout debía acotarse a un máximo de 4s; tardó {cronometro.Elapsed.TotalSeconds:0.#}s.");
    }

    [Fact]
    public async Task GenerarVistaPreviaAsync_ErrorDeRed_DevuelveFalloSinLanzarExcepcion()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            throw new HttpRequestException("DNS no resuelto: simulación de internet caído."));
        using var httpClient = new HttpClient(handler);
        using var servicio = new LabelaryPreviewService(httpClient);

        var resultado = await servicio.GenerarVistaPreviaAsync("^XA^XZ");

        Assert.False(resultado.Exito);
        Assert.Contains("Error de red", resultado.Error);
    }

    [Fact]
    public async Task GenerarVistaPreviaAsync_ZplVacio_DevuelveFalloInmediatoSinLlamarHttp()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("No debería llamarse a HTTP con ZPL vacío."));
        using var httpClient = new HttpClient(handler);
        using var servicio = new LabelaryPreviewService(httpClient);

        var resultado = await servicio.GenerarVistaPreviaAsync("   ");

        Assert.False(resultado.Exito);
        Assert.Contains("vacío", resultado.Error);
    }
}
