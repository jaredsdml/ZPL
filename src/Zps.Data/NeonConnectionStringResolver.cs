namespace Zps.Data;

/// <summary>
/// Resuelve el connection string de Neon: primero la variable de entorno DATABASE_URL,
/// y si no existe, busca un archivo .env.local subiendo desde el directorio dado hasta
/// encontrarlo. Es el mismo mecanismo que usan los scripts de Python del repositorio,
/// para no duplicar el secreto en dos formatos ni en dos convenciones distintas.
///
/// Nota: esto es adecuado para el estado actual del proyecto (equipo pequeño, secreto
/// compartido vía .env.local). Un despliegue con más de una estación o mayor exigencia
/// de seguridad debería moverlo a un almacén de secretos (Windows Credential Manager,
/// variables de entorno de máquina, etc.) en vez de un archivo de texto plano.
/// </summary>
public static class NeonConnectionStringResolver
{
    public static string Resolve(string? directorioInicial = null)
    {
        var desdeEntorno = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (!string.IsNullOrWhiteSpace(desdeEntorno))
        {
            return desdeEntorno;
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

                    if (recortada[..separador] != "DATABASE_URL")
                    {
                        continue;
                    }

                    return recortada[(separador + 1)..].Trim().Trim('"');
                }
            }

            directorio = directorio.Parent;
        }

        throw new InvalidOperationException(
            "No se encontró DATABASE_URL: defínela como variable de entorno o crea un .env.local " +
            "en algún directorio ancestro.");
    }
}
