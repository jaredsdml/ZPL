namespace Zps.Data.IntegrationTests;

/// <summary>
/// Delega en Zps.Data.NeonConnectionStringResolver (la misma resolución que usa Zps.UI en
/// producción) para no mantener dos copias de la búsqueda de .env.local.
/// </summary>
internal static class NeonTestConnection
{
    public static string GetConnectionString() => NeonConnectionStringResolver.Resolve();
}
