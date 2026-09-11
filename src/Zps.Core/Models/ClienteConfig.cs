namespace Zps.Core.Models;

/// <summary>
/// Configuración de un cliente, espejo de una entrada del diccionario CONFIG_CLIENTES
/// (config_clientes.json) en app_centralizada.py: { archivo_plantilla, mapeo_columnas }.
/// </summary>
/// <param name="Nombre">Nombre del cliente (coincide con el nombre de la hoja de Excel).</param>
/// <param name="ArchivoPlantilla">Ruta al archivo .txt con la plantilla ZPL cruda.</param>
/// <param name="MapeoColumnas">Mapa de placeholder ZPL (ej. "{SKU}") a nombre de columna del origen de datos.</param>
public sealed record ClienteConfig(
    string Nombre,
    string ArchivoPlantilla,
    IReadOnlyDictionary<string, string> MapeoColumnas);
