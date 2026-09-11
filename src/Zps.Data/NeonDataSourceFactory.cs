using Npgsql;

namespace Zps.Data;

/// <summary>
/// Crea el NpgsqlDataSource (pool de conexiones administrado por Npgsql) usado por todos
/// los servicios de Zps.Data. Un único NpgsqlDataSource debe compartirse por proceso.
/// </summary>
public static class NeonDataSourceFactory
{
    public static NpgsqlDataSource Create(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(NormalizarConnectionString(connectionString));
        return builder.Build();
    }

    public static NpgsqlDataSource Create(NeonOptions options) => Create(options.ConnectionString);

    /// <summary>
    /// Neon entrega el connection string en formato URI estilo libpq/psycopg2
    /// (postgresql://usuario:password@host/db?channel_binding=require&amp;sslmode=require,
    /// tal como aparece en .env.local). Npgsql espera un connection string ADO.NET
    /// (keyword=value;...) y no reconoce el parámetro "channel_binding", así que si
    /// detectamos un URI lo traducimos a las claves que Npgsql sí entiende. Un connection
    /// string que ya viene en formato ADO.NET se devuelve intacto.
    /// </summary>
    internal static string NormalizarConnectionString(string connectionString)
    {
        if (!connectionString.Contains("://", StringComparison.Ordinal))
        {
            return connectionString;
        }

        var uri = new Uri(connectionString);
        var partesUsuario = uri.UserInfo.Split(':', 2);
        var usuario = Uri.UnescapeDataString(partesUsuario[0]);
        var password = partesUsuario.Length > 1 ? Uri.UnescapeDataString(partesUsuario[1]) : string.Empty;
        var baseDatos = uri.AbsolutePath.TrimStart('/');
        var puerto = uri.Port == -1 ? 5432 : uri.Port;

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = puerto,
            Username = usuario,
            Password = password,
            Database = baseDatos,
            SslMode = SslMode.Require,
        };

        return builder.ConnectionString;
    }
}
