namespace Zps.Data;

/// <summary>
/// Opciones de conexión a Neon (Postgres). El proceso de host (Zps.UI / tests) es responsable
/// de resolver el connection string real (variable de entorno, .env.local, configuración, etc.);
/// esta librería no lee archivos de configuración por sí misma.
/// </summary>
public sealed record NeonOptions(string ConnectionString);
