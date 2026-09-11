using System.Net;
using Zps.Updater;

namespace Zps.Updater.Tests;

public class GitHubReleaseCheckerTests
{
    private const string RespuestaValidaConAssets = """
        {
          "tag_name": "v7.1.0",
          "body": "Notas de la version 7.1.0",
          "assets": [
            { "name": "ZPS-v7.1.0-win-x64.zip", "browser_download_url": "https://example.com/ZPS-v7.1.0-win-x64.zip" },
            { "name": "ZPS-v7.1.0-win-x64.zip.sha256", "browser_download_url": "https://example.com/ZPS-v7.1.0-win-x64.zip.sha256" }
          ]
        }
        """;

    // ---- Parseo puro de la respuesta JSON (sin red) ----

    [Fact]
    public void ParsearRespuesta_JsonValidoConAssets_ExtraeTodosLosCampos()
    {
        var resultado = GitHubReleaseChecker.ParsearRespuesta(RespuestaValidaConAssets);

        Assert.True(resultado.Exito);
        Assert.NotNull(resultado.Release);
        Assert.Equal("v7.1.0", resultado.Release!.TagName);
        Assert.Equal(new Version(7, 1, 0, 0), resultado.Release.Version);
        Assert.Equal("https://example.com/ZPS-v7.1.0-win-x64.zip", resultado.Release.ZipAssetUrl);
        Assert.Equal("ZPS-v7.1.0-win-x64.zip", resultado.Release.ZipAssetNombre);
        Assert.Equal("https://example.com/ZPS-v7.1.0-win-x64.zip.sha256", resultado.Release.ChecksumAssetUrl);
        Assert.Equal("Notas de la version 7.1.0", resultado.Release.NotasVersion);
    }

    [Fact]
    public void ParsearRespuesta_SinAssets_ExitoConUrlsNulas()
    {
        const string json = """{ "tag_name": "v1.0.0" }""";

        var resultado = GitHubReleaseChecker.ParsearRespuesta(json);

        Assert.True(resultado.Exito);
        Assert.Null(resultado.Release!.ZipAssetUrl);
        Assert.Null(resultado.Release.ChecksumAssetUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParsearRespuesta_RespuestaVacia_DevuelveFallo(string? json)
    {
        var resultado = GitHubReleaseChecker.ParsearRespuesta(json);

        Assert.False(resultado.Exito);
        Assert.Null(resultado.Release);
        Assert.Contains("vacía", resultado.Error);
    }

    [Fact]
    public void ParsearRespuesta_ObjetoJsonVacio_DevuelveFalloSinTagName()
    {
        var resultado = GitHubReleaseChecker.ParsearRespuesta("{}");

        Assert.False(resultado.Exito);
        Assert.Contains("tag_name", resultado.Error);
    }

    [Fact]
    public void ParsearRespuesta_JsonMalformado_DevuelveFalloSinLanzar()
    {
        var resultado = GitHubReleaseChecker.ParsearRespuesta("{ esto no es json valido ][");

        Assert.False(resultado.Exito);
        Assert.Contains("inválida", resultado.Error);
    }

    [Fact]
    public void ParsearRespuesta_ArrayJsonEnVezDeObjeto_DevuelveFallo()
    {
        var resultado = GitHubReleaseChecker.ParsearRespuesta("[1, 2, 3]");

        Assert.False(resultado.Exito);
    }

    [Fact]
    public void ParsearRespuesta_TagNoParseable_DevuelveFallo()
    {
        var resultado = GitHubReleaseChecker.ParsearRespuesta("""{ "tag_name": "release-experimental" }""");

        Assert.False(resultado.Exito);
        Assert.Contains("No se pudo interpretar la versión", resultado.Error);
    }

    // ---- Comportamiento de red real (con HttpMessageHandler simulado) ----

    [Fact]
    public async Task ObtenerUltimaVersionAsync_RespuestaExitosa_DevuelveElRelease()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(RespuestaValidaConAssets)
            }));
        using var httpClient = new HttpClient(handler);
        using var checker = new GitHubReleaseChecker(httpClient);

        var resultado = await checker.ObtenerUltimaVersionAsync();

        Assert.True(resultado.Exito);
        Assert.Equal("v7.1.0", resultado.Release!.TagName);
    }

    [Fact]
    public async Task ObtenerUltimaVersionAsync_EstableceUserAgent()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }));
        using var httpClient = new HttpClient(handler);
        using var checker = new GitHubReleaseChecker(httpClient);

        await checker.ObtenerUltimaVersionAsync();

        Assert.NotEmpty(handler.UltimaSolicitud!.Headers.UserAgent);
    }

    [Fact]
    public async Task ObtenerUltimaVersionAsync_ErrorHttp_DevuelveFalloConCodigoDeEstado()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { ReasonPhrase = "Not Found" }));
        using var httpClient = new HttpClient(handler);
        using var checker = new GitHubReleaseChecker(httpClient);

        var resultado = await checker.ObtenerUltimaVersionAsync();

        Assert.False(resultado.Exito);
        Assert.Contains("404", resultado.Error);
    }

    [Fact]
    public async Task ObtenerUltimaVersionAsync_ErrorDeRed_DevuelveFalloSinLanzar()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            throw new HttpRequestException("DNS no resuelto: simulación de internet caído."));
        using var httpClient = new HttpClient(handler);
        using var checker = new GitHubReleaseChecker(httpClient);

        var resultado = await checker.ObtenerUltimaVersionAsync();

        Assert.False(resultado.Exito);
        Assert.Contains("Error de red", resultado.Error);
    }

    [Fact]
    public async Task ObtenerUltimaVersionAsync_ExcedeElTimeout_DevuelveFalloSinLanzarExcepcion()
    {
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        using var checker = new GitHubReleaseChecker(httpClient, timeout: TimeSpan.FromMilliseconds(150));

        var resultado = await checker.ObtenerUltimaVersionAsync();

        Assert.False(resultado.Exito);
        Assert.Contains("Tiempo de espera agotado", resultado.Error);
    }

    [Fact]
    public async Task ObtenerUltimaVersionAsync_TimeoutSolicitadoMayorAlMaximo_SeAcotaAlMaximoPermitido()
    {
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        using var checker = new GitHubReleaseChecker(httpClient, timeout: TimeSpan.FromSeconds(30));

        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        var resultado = await checker.ObtenerUltimaVersionAsync();
        cronometro.Stop();

        Assert.False(resultado.Exito);
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(6),
            $"El timeout debía acotarse a un máximo de 5s; tardó {cronometro.Elapsed.TotalSeconds:0.#}s.");
    }
}
