using System.Text;
using Zps.Hardware;

namespace Zps.Hardware.Tests;

public class ZplBufferBuilderTests
{
    [Fact]
    public void Construir_CodificaEnUtf8()
    {
        const string zpl = "^XA^CI28^FO50,50^ADN,36,20^FDPallet #1 - Ñoño Título^FS^XZ";

        var buffer = ZplBufferBuilder.Construir(zpl);

        Assert.Equal(Encoding.UTF8.GetBytes(zpl), buffer);
    }

    [Fact]
    public void Construir_CadenaVacia_DevuelveBufferVacio()
    {
        var buffer = ZplBufferBuilder.Construir(string.Empty);

        Assert.Empty(buffer);
    }

    [Fact]
    public void Construir_PreservaLongitudDeBytesConCaracteresMultibyte()
    {
        // 'Ñ' ocupa 2 bytes en UTF-8: el conteo de bytes debe reflejarlo, no el conteo de caracteres.
        const string zpl = "Ñ";

        var buffer = ZplBufferBuilder.Construir(zpl);

        Assert.Equal(2, buffer.Length);
    }

    [Fact]
    public void Construir_ZplNulo_Lanza()
    {
        Assert.Throws<ArgumentNullException>(() => ZplBufferBuilder.Construir(null!));
    }
}
