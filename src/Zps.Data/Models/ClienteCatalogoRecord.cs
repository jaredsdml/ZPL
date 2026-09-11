namespace Zps.Data.Models;

/// <summary>
/// Fila del catálogo de clientes (tabla cat_clientes en Neon / réplica local en SQLite).
/// Sustituye a CONFIG_CLIENTES (config_clientes.json) del original en Python.
/// </summary>
public sealed record ClienteCatalogoRecord(
    string Codigo,
    string Nombre,
    bool Activo,
    string? PrefijoFolio,
    string EstrategiaLpn,
    string? PlantillaZpl,
    IReadOnlyDictionary<string, string> MapeoColumnas,
    DateTimeOffset ActualizadoEn);
