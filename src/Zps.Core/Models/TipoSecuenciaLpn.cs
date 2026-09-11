namespace Zps.Core.Models;

/// <summary>
/// Tipos de secuencia de folio LPN reconocidos por el motor de generación.
/// Corresponde 1:1 a los valores de cadena usados en GestorLPN.generar_folios
/// (app_centralizada.py): 'NORMAL', 'PNC', 'MNS_PNC_D', 'MNS_PNC_M'.
/// </summary>
public enum TipoSecuenciaLpn
{
    Normal,
    Pnc,
    MnsPncD,
    MnsPncM
}
