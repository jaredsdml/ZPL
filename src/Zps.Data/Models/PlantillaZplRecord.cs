namespace Zps.Data.Models;

/// <summary>
/// Fila de plantillas_zpl: una plantilla ZPL administrada en vivo (Neon), enlazada por el
/// nombre exacto de la hoja de Excel que la usa. Permite dar de alta o modificar el ZPL de
/// un cliente sin publicar un release — a diferencia de cat_clientes.plantilla_zpl (que sigue
/// existiendo como fallback), el código aquí usa placeholders "{{TAG}}" fijos y conocidos
/// (ver ZplPlantillaDinamicaEngine), sin necesitar un mapeo de columnas por cliente.
/// </summary>
public sealed record PlantillaZplRecord(
    int? Id,
    string HojaExcel,
    string NombreCliente,
    string CodigoZpl,
    bool Activo,
    DateTimeOffset FechaModificacion);
