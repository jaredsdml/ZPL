using Zps.Hardware;

namespace Zps.Hardware.Tests;

public class LabelaryRequestBuilderTests
{
    [Fact]
    public void ConstruirUrl_FormateaDensidadTamanoYRotacionEnLaRutaEsperada()
    {
        var url = LabelaryRequestBuilder.ConstruirUrl("8dpmm", "4x6", 0);

        Assert.Equal("https://api.labelary.com/v1/printers/8dpmm/labels/4x6/0/", url);
    }

    [Theory]
    [InlineData("6dpmm", "3x2", 90, "https://api.labelary.com/v1/printers/6dpmm/labels/3x2/90/")]
    [InlineData("12dpmm", "4x6", 180, "https://api.labelary.com/v1/printers/12dpmm/labels/4x6/180/")]
    public void ConstruirUrl_VariasCombinaciones(string densidad, string tamano, int rotacion, string urlEsperada)
    {
        var url = LabelaryRequestBuilder.ConstruirUrl(densidad, tamano, rotacion);

        Assert.Equal(urlEsperada, url);
    }

    [Fact]
    public void ConstruirUrl_DensidadVacia_Lanza()
    {
        Assert.Throws<ArgumentException>(() => LabelaryRequestBuilder.ConstruirUrl("", "4x6", 0));
    }

    [Fact]
    public async Task ConstruirContenido_EnviaElZplComoCampoFileMultipart()
    {
        const string zpl = "^XA^FDHola Mundo^FS^XZ";

        using var contenido = LabelaryRequestBuilder.ConstruirContenido(zpl);
        var textoCompleto = await contenido.ReadAsStringAsync();

        Assert.Contains(zpl, textoCompleto);
        Assert.Contains("Content-Disposition: form-data; name=file", textoCompleto);
        Assert.IsType<MultipartFormDataContent>(contenido);
    }

    [Fact]
    public void ConstruirContenido_ZplNulo_Lanza()
    {
        Assert.Throws<ArgumentNullException>(() => LabelaryRequestBuilder.ConstruirContenido(null!));
    }
}
