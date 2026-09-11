using Zps.Core;

namespace Zps.Core.Tests;

public class ZplTemplateEngineTests
{
    [Fact]
    public void Generar_ReemplazaPlaceholdersDeColumnasEIndiceTotal()
    {
        const string plantilla = "^XA^FD{LPN} {SKU} {INDICE}/{TOTAL}^FS^XZ";
        var mapeo = new Dictionary<string, string> { ["{LPN}"] = "LPN", ["{SKU}"] = "SKU" };
        var datos = new Dictionary<string, string?> { ["LPN"] = "LGMA20260000001", ["SKU"] = "ABC123" };

        var resultado = ZplTemplateEngine.Generar(plantilla, mapeo, datos, indiceActual: 3, totalFilas: 10);

        Assert.Equal("^XA^CI28^FDLGMA20260000001 ABC123 3/10^FS^XZ", resultado);
    }

    [Fact]
    public void Generar_AgregaCI28SoloSiNoEstaPresente()
    {
        const string plantillaConCi28 = "^XA^CI28^FD{SKU}^FS^XZ";
        var resultado = ZplTemplateEngine.Generar(plantillaConCi28, new Dictionary<string, string> { ["{SKU}"] = "SKU" }, new Dictionary<string, string?> { ["SKU"] = "X" });

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(resultado, "\\^CI28"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nan")]
    [InlineData("NaN")]
    [InlineData("")]
    public void Generar_ValorAusenteONan_SeReemplazaPorCadenaVacia(string? valorCrudo)
    {
        const string plantilla = "^XA^FDSKU:{SKU}^FS^XZ";
        var mapeo = new Dictionary<string, string> { ["{SKU}"] = "SKU" };
        var datos = new Dictionary<string, string?> { ["SKU"] = valorCrudo };

        var resultado = ZplTemplateEngine.Generar(plantilla, mapeo, datos);

        Assert.Equal("^XA^CI28^FDSKU:^FS^XZ", resultado);
    }

    [Fact]
    public void Generar_ColumnaDelMapeoNoExisteEnDatos_SeReemplazaPorCadenaVacia()
    {
        const string plantilla = "^XA^FD{LOTE}^FS^XZ";
        var mapeo = new Dictionary<string, string> { ["{LOTE}"] = "LOTE" };
        var datos = new Dictionary<string, string?>(); // la fila no trae columna LOTE

        var resultado = ZplTemplateEngine.Generar(plantilla, mapeo, datos);

        Assert.Equal("^XA^CI28^FD^FS^XZ", resultado);
    }
}
