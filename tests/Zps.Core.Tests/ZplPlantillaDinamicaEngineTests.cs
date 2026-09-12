using Zps.Core;

namespace Zps.Core.Tests;

public class ZplPlantillaDinamicaEngineTests
{
    [Fact]
    public void Generar_ReemplazaTodosLosTagsConocidos()
    {
        const string plantilla =
            "^XA^FD{{LPN}} {{CONSECUTIVO}} {{SKU}} {{LOTE}} {{CANTIDAD}} {{CAJAS}} {{TARIMA_ACTUAL}}/{{TARIMA_TOTAL}} {{ARRIBO}} {{SOLICITANTE}}^FS^XZ";
        var datos = new Dictionary<string, string?>
        {
            ["SKU"] = "ABC123",
            ["LOTE"] = "L-9",
            ["CANTIDAD"] = "36",
            ["CAJAS"] = "NV",
        };

        var resultado = ZplPlantillaDinamicaEngine.Generar(
            plantilla, datos, lpn: "LGMA20260000001", consecutivo: 7,
            tarimaActual: 3, tarimaTotal: 10, arribo: "ARRIBO-1", solicitante: "JUAN");

        Assert.Equal(
            "^XA^CI28^FDLGMA20260000001 7 ABC123 L-9 36 NV 3/10 ARRIBO-1 JUAN^FS^XZ",
            resultado);
    }

    [Fact]
    public void Generar_AgregaCI28SoloSiNoEstaPresente()
    {
        const string plantillaConCi28 = "^XA^CI28^FD{{SKU}}^FS^XZ";
        var resultado = ZplPlantillaDinamicaEngine.Generar(
            plantillaConCi28, new Dictionary<string, string?> { ["SKU"] = "X" },
            lpn: null, consecutivo: null, tarimaActual: 1, tarimaTotal: 1, arribo: null, solicitante: null);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(resultado, "\\^CI28"));
    }

    [Fact]
    public void Generar_CantidadCaeAQtySiCantidadFalta()
    {
        const string plantilla = "^XA^FD{{CANTIDAD}}^FS^XZ";
        var datos = new Dictionary<string, string?> { ["QTY"] = "42" };

        var resultado = ZplPlantillaDinamicaEngine.Generar(
            plantilla, datos, lpn: null, consecutivo: null, tarimaActual: 1, tarimaTotal: 1, arribo: null, solicitante: null);

        Assert.Equal("^XA^CI28^FD42^FS^XZ", resultado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nan")]
    [InlineData("")]
    public void Generar_ColumnaAusenteONan_SeReemplazaPorCadenaVacia(string? valorCrudo)
    {
        const string plantilla = "^XA^FDSKU:{{SKU}}^FS^XZ";
        var datos = new Dictionary<string, string?> { ["SKU"] = valorCrudo };

        var resultado = ZplPlantillaDinamicaEngine.Generar(
            plantilla, datos, lpn: null, consecutivo: null, tarimaActual: 1, tarimaTotal: 1, arribo: null, solicitante: null);

        Assert.Equal("^XA^CI28^FDSKU:^FS^XZ", resultado);
    }

    [Fact]
    public void Generar_ConsecutivoNulo_SeReemplazaPorCadenaVacia()
    {
        const string plantilla = "^XA^FD{{CONSECUTIVO}}^FS^XZ";

        var resultado = ZplPlantillaDinamicaEngine.Generar(
            plantilla, new Dictionary<string, string?>(), lpn: null, consecutivo: null,
            tarimaActual: 1, tarimaTotal: 1, arribo: null, solicitante: null);

        Assert.Equal("^XA^CI28^FD^FS^XZ", resultado);
    }

    // ---- Tags arbitrarios: un cliente nuevo puede traer columnas propias (ATTR,
    // PEDIMENTO, NUMERO, etc.) que no están en el set de tags conocidos — deben resolverse
    // igual, por búsqueda directa contra la fila, sin necesitar cambios en el motor. ----

    [Fact]
    public void Generar_TagArbitrarioNoConocido_SeResuelveContraLaFila()
    {
        const string plantilla = "^XA^FD{{ATTR}} {{PEDIMENTO}} {{NUMERO}}^FS^XZ";
        var datos = new Dictionary<string, string?>
        {
            ["ATTR"] = "ROJO",
            ["PEDIMENTO"] = "PED-001",
            ["NUMERO"] = "42",
        };

        var resultado = ZplPlantillaDinamicaEngine.Generar(
            plantilla, datos, lpn: null, consecutivo: null, tarimaActual: 1, tarimaTotal: 1, arribo: null, solicitante: null);

        Assert.Equal("^XA^CI28^FDROJO PED-001 42^FS^XZ", resultado);
    }

    [Fact]
    public void Generar_TagArbitrarioAusente_SeReemplazaPorCadenaVacia()
    {
        const string plantilla = "^XA^FD{{ATRIBUTO}}^FS^XZ";

        var resultado = ZplPlantillaDinamicaEngine.Generar(
            plantilla, new Dictionary<string, string?>(), lpn: null, consecutivo: null,
            tarimaActual: 1, tarimaTotal: 1, arribo: null, solicitante: null);

        Assert.Equal("^XA^CI28^FD^FS^XZ", resultado);
    }

    [Fact]
    public void Generar_ValorDeColumnaConEspacios_SeAplicaTrim()
    {
        const string plantilla = "^XA^FD{{SKU}}^FS^XZ";
        var datos = new Dictionary<string, string?> { ["SKU"] = "  ABC123  " };

        var resultado = ZplPlantillaDinamicaEngine.Generar(
            plantilla, datos, lpn: null, consecutivo: null, tarimaActual: 1, tarimaTotal: 1, arribo: null, solicitante: null);

        Assert.Equal("^XA^CI28^FDABC123^FS^XZ", resultado);
    }
}
