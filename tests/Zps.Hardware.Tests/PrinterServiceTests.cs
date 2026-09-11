using System.Diagnostics;
using Zps.Hardware;

namespace Zps.Hardware.Tests;

/// <summary>
/// PrinterService se prueba con el descubrimiento y el envío RAW inyectados (constructor
/// interno + InternalsVisibleTo), para no depender del spooler físico de Windows ni de
/// qué impresoras existan en la máquina que corre las pruebas.
/// </summary>
public class PrinterServiceTests
{
    [Fact]
    public void ExisteImpresora_NombreInexistente_DevuelveFalse()
    {
        using var servicio = new PrinterService(() => new[] { "Zebra ZDesigner 450" }, (_, _, _) => { });

        Assert.False(servicio.ExisteImpresora("Impresora Fantasma"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExisteImpresora_NombreNuloOVacio_DevuelveFalse(string? nombre)
    {
        using var servicio = new PrinterService(() => new[] { "Zebra ZDesigner 450" }, (_, _, _) => { });

        Assert.False(servicio.ExisteImpresora(nombre));
    }

    [Fact]
    public void ExisteImpresora_ComparacionInsensibleAMayusculas()
    {
        using var servicio = new PrinterService(() => new[] { "Zebra ZDesigner 450" }, (_, _, _) => { });

        Assert.True(servicio.ExisteImpresora("zebra zdesigner 450"));
        Assert.True(servicio.ExisteImpresora("ZEBRA ZDESIGNER 450"));
    }

    [Fact]
    public async Task EncolarAsync_ImpresoraInexistente_DevuelveFalloYNuncaInvocaElEnvio()
    {
        var seInvocoEnvio = false;
        using var servicio = new PrinterService(
            () => new[] { "Zebra ZDesigner 450" },
            (_, _, _) => seInvocoEnvio = true);

        var resultado = await servicio.EncolarAsync("Impresora Que No Existe", "^XA^XZ");

        Assert.False(resultado.Exito);
        Assert.Contains("no existe en el spooler", resultado.Error);
        Assert.False(seInvocoEnvio, "La validación de existencia debe ocurrir ANTES de despachar cualquier byte.");
    }

    [Fact]
    public async Task EncolarAsync_ImpresoraValida_InvocaElEnvioConLosDatosCorrectosYDevuelveExito()
    {
        string? impresoraRecibida = null;
        string? zplRecibido = null;

        using var servicio = new PrinterService(
            () => new[] { "Zebra ZDesigner 450" },
            (impresora, zpl, _) =>
            {
                impresoraRecibida = impresora;
                zplRecibido = zpl;
            });

        var resultado = await servicio.EncolarAsync("Zebra ZDesigner 450", "^XA^FDHola^FS^XZ");

        Assert.True(resultado.Exito);
        Assert.Null(resultado.Error);
        Assert.Equal("Zebra ZDesigner 450", impresoraRecibida);
        Assert.Equal("^XA^FDHola^FS^XZ", zplRecibido);
    }

    [Fact]
    public async Task EncolarAsync_SiElEnvioLanza_DevuelveFalloConElMensajeDeLaExcepcion()
    {
        using var servicio = new PrinterService(
            () => new[] { "Zebra ZDesigner 450" },
            (_, _, _) => throw new RawPrintException("El spooler rechazó el trabajo.", 1801));

        var resultado = await servicio.EncolarAsync("Zebra ZDesigner 450", "^XA^XZ");

        Assert.False(resultado.Exito);
        Assert.Contains("El spooler rechazó el trabajo.", resultado.Error);
    }

    [Fact]
    public async Task EncolarAsync_NoBloqueaElHiloLlamador_AunqueElEnvioSeaLento()
    {
        using var servicio = new PrinterService(
            () => new[] { "Zebra ZDesigner 450" },
            (_, _, _) => Thread.Sleep(500)); // simula un envío lento al spooler

        var cronometro = Stopwatch.StartNew();
        var tareaResultado = servicio.EncolarAsync("Zebra ZDesigner 450", "^XA^XZ");
        cronometro.Stop();

        // EncolarAsync debe retornar casi de inmediato: solo encola, no espera al envío.
        Assert.True(cronometro.ElapsedMilliseconds < 200,
            $"EncolarAsync tardó {cronometro.ElapsedMilliseconds}ms en retornar; debería ser no bloqueante.");

        var resultado = await tareaResultado;
        Assert.True(resultado.Exito);
    }

    [Fact]
    public async Task EncolarAsync_VariosTrabajosConcurrentes_TodosSeProcesanCorrectamente()
    {
        var trabajosProcesados = new System.Collections.Concurrent.ConcurrentBag<string>();

        using var servicio = new PrinterService(
            () => new[] { "Zebra ZDesigner 450" },
            (_, zpl, _) => trabajosProcesados.Add(zpl));

        var tareas = Enumerable.Range(0, 10)
            .Select(i => servicio.EncolarAsync("Zebra ZDesigner 450", $"ETIQUETA-{i}"))
            .ToArray();

        var resultados = await Task.WhenAll(tareas);

        Assert.All(resultados, r => Assert.True(r.Exito));
        Assert.Equal(10, trabajosProcesados.Count);
        for (var i = 0; i < 10; i++)
        {
            Assert.Contains($"ETIQUETA-{i}", trabajosProcesados);
        }
    }
}
