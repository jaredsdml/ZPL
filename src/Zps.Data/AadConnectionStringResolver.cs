using Microsoft.Data.SqlClient;

namespace Zps.Data;

/// <summary>
/// Resuelve el connection string de SQL Server hacia HighJump/AAD (192.168.100.76, solo
/// lectura): primero AAD_DB_CONNECTION_STRING completo (variable de entorno o .env.local,
/// mismo mecanismo de búsqueda que NeonConnectionStringResolver), y si no está definido, lo
/// arma a partir de las variables individuales AAD_DB_SERVER / AAD_DB_NAME / AAD_DB_USER /
/// AAD_DB_PASSWORD.
/// </summary>
public static class AadConnectionStringResolver
{
    private static readonly string[] Claves =
    [
        "AAD_DB_CONNECTION_STRING",
        "AAD_DB_SERVER",
        "AAD_DB_NAME",
        "AAD_DB_USER",
        "AAD_DB_PASSWORD",
    ];

    public static string Resolve(string? directorioInicial = null)
    {
        var valores = LeerValores(directorioInicial);

        if (valores.TryGetValue("AAD_DB_CONNECTION_STRING", out var completo) && !string.IsNullOrWhiteSpace(completo))
        {
            return completo;
        }

        if (!valores.TryGetValue("AAD_DB_SERVER", out var servidor) || string.IsNullOrWhiteSpace(servidor))
        {
            throw new InvalidOperationException(
                "No se encontró AAD_DB_CONNECTION_STRING ni AAD_DB_SERVER: define alguno como variable de " +
                "entorno o en un .env.local en algún directorio ancestro.");
        }

        valores.TryGetValue("AAD_DB_NAME", out var baseDatos);
        valores.TryGetValue("AAD_DB_USER", out var usuario);
        valores.TryGetValue("AAD_DB_PASSWORD", out var password);

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = servidor,
            InitialCatalog = baseDatos ?? string.Empty,
            UserID = usuario ?? string.Empty,
            Password = password ?? string.Empty,
            TrustServerCertificate = true,
            ConnectTimeout = 5,
        };

        return builder.ConnectionString;
    }

    private static Dictionary<string, string> LeerValores(string? directorioInicial)
    {
        var resultado = new Dictionary<string, string>();

        foreach (var clave in Claves)
        {
            var desdeEntorno = Environment.GetEnvironmentVariable(clave);
            if (!string.IsNullOrWhiteSpace(desdeEntorno))
            {
                resultado[clave] = desdeEntorno;
            }
        }

        if (resultado.Count == Claves.Length)
        {
            return resultado;
        }

        var directorio = new DirectoryInfo(directorioInicial ?? AppContext.BaseDirectory);
        while (directorio is not null)
        {
            var candidato = Path.Combine(directorio.FullName, ".env.local");
            if (File.Exists(candidato))
            {
                foreach (var linea in File.ReadAllLines(candidato))
                {
                    var recortada = linea.Trim();
                    if (recortada.Length == 0 || recortada.StartsWith('#'))
                    {
                        continue;
                    }

                    var separador = recortada.IndexOf('=');
                    if (separador <= 0)
                    {
                        continue;
                    }

                    var clave = recortada[..separador];
                    if (!Claves.Contains(clave) || resultado.ContainsKey(clave))
                    {
                        continue;
                    }

                    resultado[clave] = recortada[(separador + 1)..].Trim().Trim('"');
                }

                break;
            }

            directorio = directorio.Parent;
        }

        return resultado;
    }
}
