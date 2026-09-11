using System.Globalization;
using System.Text.RegularExpressions;
using Zps.Core.Models;

namespace Zps.Core;

/// <summary>
/// Motor de dominio para la generación y validación de folios LPN.
/// Puerto fiel de la lógica contenida en GestorLPN.generar_folios (app_centralizada.py, Python),
/// sin ninguna dependencia de UI, base de datos ni impresión.
/// </summary>
public static class MinisoLpnEngine
{
    /// <summary>
    /// Motivos válidos para Categoría D (Devolución), tal como en el original:
    /// "if motivo_clean in [1, 2, 3, 4, 5, 12, 13, 14]".
    /// </summary>
    private static readonly IReadOnlySet<int> MotivosValidosCategoriaD =
        new HashSet<int> { 1, 2, 3, 4, 5, 12, 13, 14 };

    private static readonly Regex PrimerNumero = new(@"\d+", RegexOptions.Compiled);

    /// <summary>
    /// Clasifica una fila de un cliente MINISO. La regla de negocio real es que el régimen
    /// especial de Categoría/Motivo (PNC-D / PNC-M) solo aplica a filas que efectivamente
    /// traen ese dato; una fila sin CATEGORIA ni ITEM no es un error de captura, es
    /// simplemente un folio NORMAL corriente que no participa del régimen MINISO:
    ///
    ///   si CATEGORIA e ITEM están ambos vacíos/en blanco -> válido, tipo NORMAL, sin motivo.
    ///   si CATEGORIA o ITEM traen algún valor -> aplica la validación estricta original:
    ///     cat = str(row.get('CATEGORIA', '')).strip().upper()
    ///     motivo_raw = str(row.get('ITEM', row.get('MOTIVO', '')))
    ///     match = re.search('\d+', motivo_raw)
    ///     motivo_clean = int(match.group()) if match else None
    ///     if motivo_clean is None: error "No hay número de ITEM válido."
    ///     elif 'D' in cat: motivo in {1,2,3,4,5,12,13,14} -> MNS_PNC_D, si no error
    ///     elif 'M' in cat: 6&lt;=motivo&lt;=11 or motivo==15 -> MNS_PNC_M, si no error
    ///     else: error "Categoría '{cat}' desconocida."
    /// </summary>
    /// <param name="categoria">Valor crudo de la columna CATEGORIA.</param>
    /// <param name="itemOMotivoRaw">Valor crudo de la columna ITEM (o MOTIVO como respaldo).</param>
    public static MinisoValidationResult ValidarCategoriaMotivo(string? categoria, string? itemOMotivoRaw)
    {
        var catCruda = (categoria ?? string.Empty).Trim();
        var itemCrudo = (itemOMotivoRaw ?? string.Empty).Trim();

        // Fila sin CATEGORIA ni ITEM: no participa del régimen especial de MINISO.
        // No es un error de captura, es un folio NORMAL corriente.
        if (catCruda.Length == 0 && itemCrudo.Length == 0)
        {
            return MinisoValidationResult.Valido(TipoSecuenciaLpn.Normal);
        }

        var cat = catCruda.ToUpperInvariant();
        var match = PrimerNumero.Match(itemCrudo);

        if (!match.Success)
        {
            return MinisoValidationResult.Invalido("No hay número de ITEM válido.");
        }

        var motivo = int.Parse(match.Value, CultureInfo.InvariantCulture);

        if (cat.Contains('D'))
        {
            return MotivosValidosCategoriaD.Contains(motivo)
                ? MinisoValidationResult.Valido(TipoSecuenciaLpn.MnsPncD, motivo)
                : MinisoValidationResult.Invalido("Cat D requiere Motivos 1-5, 12-14.", motivo);
        }

        if (cat.Contains('M'))
        {
            return motivo is >= 6 and <= 11 or 15
                ? MinisoValidationResult.Valido(TipoSecuenciaLpn.MnsPncM, motivo)
                : MinisoValidationResult.Invalido("Cat M requiere Motivos 6-11 o 15.", motivo);
        }

        return MinisoValidationResult.Invalido($"Categoría '{cat}' desconocida.", motivo);
    }

    /// <summary>
    /// Determina el tipo de secuencia estándar (fuera del régimen MINISO), espejo de:
    ///   tipo_secuencia = 'PNC' if 'PNC' in valor_lpn else 'NORMAL'
    /// </summary>
    public static TipoSecuenciaLpn DeterminarTipoEstandar(string? valorLpnExistente)
    {
        return (valorLpnExistente ?? string.Empty).Contains("PNC", StringComparison.OrdinalIgnoreCase)
            ? TipoSecuenciaLpn.Pnc
            : TipoSecuenciaLpn.Normal;
    }

    /// <summary>
    /// Clave de secuencia usada para el contador de consecutivos (tabla secuencias_lpn en Neon,
    /// caché maximos_locales en el original). NORMAL y PNC llevan año; los tipos MNS_PNC_* no,
    /// espejo de: f'{tipo_secuencia}_{anio}' if tipo_secuencia in ('NORMAL', 'PNC') else tipo_secuencia
    /// </summary>
    public static string ClaveSecuencia(TipoSecuenciaLpn tipo, int anio)
    {
        return tipo switch
        {
            TipoSecuenciaLpn.Normal => $"NORMAL_{anio}",
            TipoSecuenciaLpn.Pnc => $"PNC_{anio}",
            TipoSecuenciaLpn.MnsPncD => "MNS_PNC_D",
            TipoSecuenciaLpn.MnsPncM => "MNS_PNC_M",
            _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de secuencia no soportado.")
        };
    }

    /// <summary>
    /// Formatea el folio final dado su tipo, consecutivo, año (para NORMAL/PNC) y motivo
    /// (obligatorio para MNS_PNC_D/MNS_PNC_M). Espejo exacto de:
    ///   NORMAL     -> f'LGMA{anio}{nuevo_num:07d}'
    ///   PNC        -> f'PNC{anio}{nuevo_num:08d}'
    ///   MNS_PNC_D  -> f'PNCD{motivo_clean:02d}-{nuevo_num:05d}'
    ///   MNS_PNC_M  -> f'PNCM{motivo_clean:02d}-{nuevo_num:05d}'
    /// </summary>
    public static string FormatearFolio(TipoSecuenciaLpn tipo, int consecutivo, int anio, int? motivo = null)
    {
        if (consecutivo < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(consecutivo), consecutivo, "El consecutivo no puede ser negativo.");
        }

        return tipo switch
        {
            TipoSecuenciaLpn.Normal => $"LGMA{anio}{consecutivo.ToString("D7", CultureInfo.InvariantCulture)}",
            TipoSecuenciaLpn.Pnc => $"PNC{anio}{consecutivo.ToString("D8", CultureInfo.InvariantCulture)}",
            TipoSecuenciaLpn.MnsPncD => $"PNCD{RequireMotivo(motivo, tipo).ToString("D2", CultureInfo.InvariantCulture)}-{consecutivo.ToString("D5", CultureInfo.InvariantCulture)}",
            TipoSecuenciaLpn.MnsPncM => $"PNCM{RequireMotivo(motivo, tipo).ToString("D2", CultureInfo.InvariantCulture)}-{consecutivo.ToString("D5", CultureInfo.InvariantCulture)}",
            _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de secuencia no soportado.")
        };
    }

    private static int RequireMotivo(int? motivo, TipoSecuenciaLpn tipo)
    {
        if (motivo is null)
        {
            throw new ArgumentException($"El tipo {tipo} requiere un motivo numérico para formatear el folio.", nameof(motivo));
        }

        return motivo.Value;
    }
}
