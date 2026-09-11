using Zps.Core;
using Zps.Core.Models;

namespace Zps.Core.Tests;

public class MinisoLpnEngineTests
{
    // ---- Categoría D: motivos válidos {1,2,3,4,5,12,13,14} ----

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void ValidarCategoriaMotivo_CategoriaD_MotivoValido_RetornaMnsPncD(int motivo)
    {
        var resultado = MinisoLpnEngine.ValidarCategoriaMotivo("D", motivo.ToString());

        Assert.True(resultado.EsValido);
        Assert.Equal(TipoSecuenciaLpn.MnsPncD, resultado.TipoSecuencia);
        Assert.Equal(motivo, resultado.Motivo);
        Assert.Null(resultado.MensajeError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(11)]
    [InlineData(15)]
    [InlineData(20)]
    public void ValidarCategoriaMotivo_CategoriaD_MotivoInvalido_RetornaError(int motivo)
    {
        var resultado = MinisoLpnEngine.ValidarCategoriaMotivo("D", motivo.ToString());

        Assert.False(resultado.EsValido);
        Assert.Null(resultado.TipoSecuencia);
        Assert.Equal("Cat D requiere Motivos 1-5, 12-14.", resultado.MensajeError);
    }

    // ---- Categoría M: motivos válidos 6..11 o 15 ----

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(15)]
    public void ValidarCategoriaMotivo_CategoriaM_MotivoValido_RetornaMnsPncM(int motivo)
    {
        var resultado = MinisoLpnEngine.ValidarCategoriaMotivo("M", motivo.ToString());

        Assert.True(resultado.EsValido);
        Assert.Equal(TipoSecuenciaLpn.MnsPncM, resultado.TipoSecuencia);
        Assert.Equal(motivo, resultado.Motivo);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(14)]
    [InlineData(16)]
    public void ValidarCategoriaMotivo_CategoriaM_MotivoInvalido_RetornaError(int motivo)
    {
        var resultado = MinisoLpnEngine.ValidarCategoriaMotivo("M", motivo.ToString());

        Assert.False(resultado.EsValido);
        Assert.Equal("Cat M requiere Motivos 6-11 o 15.", resultado.MensajeError);
    }

    // ---- Categoría desconocida y motivo ausente ----

    [Theory]
    [InlineData("X")]
    [InlineData("Z")]
    [InlineData("")]
    public void ValidarCategoriaMotivo_CategoriaDesconocida_RetornaError(string categoria)
    {
        var resultado = MinisoLpnEngine.ValidarCategoriaMotivo(categoria, "7");

        Assert.False(resultado.EsValido);
        Assert.Equal($"Categoría '{categoria}' desconocida.", resultado.MensajeError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SIN NUMERO")]
    [InlineData(null)]
    public void ValidarCategoriaMotivo_SinNumeroEnItem_RetornaError(string? itemOMotivoRaw)
    {
        var resultado = MinisoLpnEngine.ValidarCategoriaMotivo("D", itemOMotivoRaw);

        Assert.False(resultado.EsValido);
        Assert.Null(resultado.Motivo);
        Assert.Equal("No hay número de ITEM válido.", resultado.MensajeError);
    }

    // ---- Filas sin CATEGORIA ni ITEM: no participan del régimen MINISO, son NORMAL válido ----

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData(null, "")]
    [InlineData("", null)]
    public void ValidarCategoriaMotivo_SinCategoriaNiItem_EsNormalValidoSinError(string? categoria, string? item)
    {
        var resultado = MinisoLpnEngine.ValidarCategoriaMotivo(categoria, item);

        Assert.True(resultado.EsValido);
        Assert.Equal(TipoSecuenciaLpn.Normal, resultado.TipoSecuencia);
        Assert.Null(resultado.Motivo);
        Assert.Null(resultado.MensajeError);
    }

    [Fact]
    public void ValidarCategoriaMotivo_SoloCategoriaSinItem_SiValidaEstrictoYFalla()
    {
        // A diferencia del caso "ambos vacíos", si CATEGORIA trae algo (aunque ITEM no),
        // la fila sí entra al régimen MINISO y el ITEM faltante es un error real.
        var resultado = MinisoLpnEngine.ValidarCategoriaMotivo("D", "");

        Assert.False(resultado.EsValido);
        Assert.Equal("No hay número de ITEM válido.", resultado.MensajeError);
    }

    [Fact]
    public void ValidarCategoriaMotivo_SoloItemSinCategoria_SiValidaEstrictoYFalla()
    {
        // Igual que arriba pero al revés: ITEM con valor y CATEGORIA vacía sí es un error
        // (categoría desconocida), porque al menos uno de los dos campos trae dato.
        var resultado = MinisoLpnEngine.ValidarCategoriaMotivo("", "7");

        Assert.False(resultado.EsValido);
        Assert.Equal("Categoría '' desconocida.", resultado.MensajeError);
    }

    [Fact]
    public void ValidarCategoriaMotivo_EscenarioMixto_ClasificaCadaFilaCorrectamente()
    {
        // Simula un lote real: filas NORMAL (sin categoría/item) mezcladas con filas PNC
        // válidas e inválidas, tal como llegaría un Excel de MINISO con datos parciales.
        var filas = new (string? Categoria, string? Item)[]
        {
            (null, null),      // NORMAL, válida
            ("", ""),          // NORMAL, válida
            ("D", "3"),        // PNC-D válida
            ("M", "9"),        // PNC-M válida
            ("D", "99"),       // PNC-D inválida (motivo fuera de rango)
            ("X", "1"),        // categoría desconocida, inválida
        };

        var resultados = filas.Select(f => MinisoLpnEngine.ValidarCategoriaMotivo(f.Categoria, f.Item)).ToList();

        Assert.True(resultados[0].EsValido);
        Assert.Equal(TipoSecuenciaLpn.Normal, resultados[0].TipoSecuencia);

        Assert.True(resultados[1].EsValido);
        Assert.Equal(TipoSecuenciaLpn.Normal, resultados[1].TipoSecuencia);

        Assert.True(resultados[2].EsValido);
        Assert.Equal(TipoSecuenciaLpn.MnsPncD, resultados[2].TipoSecuencia);
        Assert.Equal(3, resultados[2].Motivo);

        Assert.True(resultados[3].EsValido);
        Assert.Equal(TipoSecuenciaLpn.MnsPncM, resultados[3].TipoSecuencia);
        Assert.Equal(9, resultados[3].Motivo);

        Assert.False(resultados[4].EsValido);
        Assert.False(resultados[5].EsValido);

        // Solo 2 de las 6 filas requieren reservar contra las claves MNS_PNC_*; las 2
        // NORMAL deben reservarse contra la secuencia estándar (NORMAL_{año}), y las 2
        // inválidas no deberían generarse en absoluto.
        var validas = resultados.Where(r => r.EsValido).ToList();
        Assert.Equal(2, validas.Count(r => r.TipoSecuencia == TipoSecuenciaLpn.Normal));
        Assert.Equal(1, validas.Count(r => r.TipoSecuencia == TipoSecuenciaLpn.MnsPncD));
        Assert.Equal(1, validas.Count(r => r.TipoSecuencia == TipoSecuenciaLpn.MnsPncM));
    }

    [Fact]
    public void ValidarCategoriaMotivo_ExtraeElPrimerNumeroDeCadenaMixta()
    {
        // Espejo de re.search('\d+', motivo_raw): toma el primer bloque de dígitos encontrado.
        var resultado = MinisoLpnEngine.ValidarCategoriaMotivo("D", "SKU-004-ABC");

        Assert.True(resultado.EsValido);
        Assert.Equal(4, resultado.Motivo);
    }

    [Fact]
    public void ValidarCategoriaMotivo_CategoriaSensibleAMayusculasYEspacios()
    {
        var resultado = MinisoLpnEngine.ValidarCategoriaMotivo("  d  ", "12");

        Assert.True(resultado.EsValido);
        Assert.Equal(TipoSecuenciaLpn.MnsPncD, resultado.TipoSecuencia);
    }

    // ---- Determinación de tipo estándar (no-MINISO) ----

    [Theory]
    [InlineData("ABC-PNC-000123", true)]
    [InlineData("pnc12345", true)]
    [InlineData("LGMA20260000001", false)]
    [InlineData("", false)]
    public void DeterminarTipoEstandar_DetectaSubcadenaPnc(string valorLpn, bool esperaPnc)
    {
        var tipo = MinisoLpnEngine.DeterminarTipoEstandar(valorLpn);

        Assert.Equal(esperaPnc ? TipoSecuenciaLpn.Pnc : TipoSecuenciaLpn.Normal, tipo);
    }

    // ---- Clave de secuencia (contador secuencias_lpn) ----

    [Fact]
    public void ClaveSecuencia_NormalYPnc_IncluyenAnio()
    {
        Assert.Equal("NORMAL_2026", MinisoLpnEngine.ClaveSecuencia(TipoSecuenciaLpn.Normal, 2026));
        Assert.Equal("PNC_2026", MinisoLpnEngine.ClaveSecuencia(TipoSecuenciaLpn.Pnc, 2026));
    }

    [Fact]
    public void ClaveSecuencia_TiposMiniso_NoIncluyenAnio()
    {
        Assert.Equal("MNS_PNC_D", MinisoLpnEngine.ClaveSecuencia(TipoSecuenciaLpn.MnsPncD, 2026));
        Assert.Equal("MNS_PNC_M", MinisoLpnEngine.ClaveSecuencia(TipoSecuenciaLpn.MnsPncM, 2026));
    }

    // ---- Formateo estricto de folios ----

    [Fact]
    public void FormatearFolio_Normal_UsaSieteDigitosDeConsecutivo()
    {
        var folio = MinisoLpnEngine.FormatearFolio(TipoSecuenciaLpn.Normal, 42, 2026);

        Assert.Equal("LGMA20260000042", folio);
    }

    [Fact]
    public void FormatearFolio_Pnc_UsaOchoDigitosDeConsecutivo()
    {
        var folio = MinisoLpnEngine.FormatearFolio(TipoSecuenciaLpn.Pnc, 7, 2026);

        Assert.Equal("PNC202600000007", folio);
    }

    [Fact]
    public void FormatearFolio_MnsPncD_FormatoMotivoGuionConsecutivo()
    {
        var folio = MinisoLpnEngine.FormatearFolio(TipoSecuenciaLpn.MnsPncD, 5, 2026, motivo: 3);

        Assert.Equal("PNCD03-00005", folio);
    }

    [Fact]
    public void FormatearFolio_MnsPncM_FormatoMotivoGuionConsecutivo()
    {
        var folio = MinisoLpnEngine.FormatearFolio(TipoSecuenciaLpn.MnsPncM, 12, 2026, motivo: 9);

        Assert.Equal("PNCM09-00012", folio);
    }

    [Fact]
    public void FormatearFolio_MnsPncD_SinMotivo_Lanza()
    {
        Assert.Throws<ArgumentException>(() =>
            MinisoLpnEngine.FormatearFolio(TipoSecuenciaLpn.MnsPncD, 5, 2026));
    }

    [Fact]
    public void FormatearFolio_MnsPncM_SinMotivo_Lanza()
    {
        Assert.Throws<ArgumentException>(() =>
            MinisoLpnEngine.FormatearFolio(TipoSecuenciaLpn.MnsPncM, 5, 2026));
    }

    [Fact]
    public void FormatearFolio_ConsecutivoNegativo_Lanza()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MinisoLpnEngine.FormatearFolio(TipoSecuenciaLpn.Normal, -1, 2026));
    }
}
