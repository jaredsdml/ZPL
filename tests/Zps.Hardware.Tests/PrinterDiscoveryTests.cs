using Zps.Hardware;

namespace Zps.Hardware.Tests;

/// <summary>
/// A diferencia del resto de la suite (que inyecta el descubrimiento para no depender del
/// entorno), esta prueba llama a PrinterDiscovery.ListarInstaladas() de verdad: habla con
/// el servicio de cola de impresión (spooler) local de Windows, que siempre está disponible
/// en una máquina Windows (a diferencia de una impresora Zebra física o de la red). Sirve
/// para detectar errores de marshaling en el P/Invoke (offsets de struct, tamaños, etc.)
/// que un descubrimiento simulado nunca podría revelar.
/// </summary>
public class PrinterDiscoveryTests
{
    [Fact]
    public void ListarInstaladas_ContraElSpoolerReal_NoLanzaYDevuelveNombresNoVacios()
    {
        var impresoras = PrinterDiscovery.ListarInstaladas();

        Assert.NotNull(impresoras);
        Assert.All(impresoras, nombre => Assert.False(string.IsNullOrWhiteSpace(nombre)));
    }
}
