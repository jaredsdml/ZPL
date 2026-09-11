using System.Globalization;

namespace Zps.Updater;

/// <summary>
/// Parseo y comparación de versiones tipo SemVer para tags de GitHub Releases (p. ej.
/// "v7.0.1"). Se apoya en System.Version (Major.Minor.Build.Revision) porque así es como
/// el propio requerimiento compara la versión del ensamblado
/// (Assembly.GetExecutingAssembly().GetName().Version); por eso cualquier sufijo de
/// prerelease/metadata SemVer (p. ej. "-beta.1", "+build5") se descarta al parsear: System.Version
/// no puede representar precedencia de prerelease, así que solo se compara la parte numérica.
/// </summary>
public static class SemVer
{
    public static bool TryParse(string? tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);

        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var texto = tag.Trim();
        if (texto.Length > 0 && (texto[0] == 'v' || texto[0] == 'V'))
        {
            texto = texto[1..];
        }

        var indiceCorte = texto.IndexOfAny(['-', '+']);
        if (indiceCorte >= 0)
        {
            texto = texto[..indiceCorte];
        }

        if (texto.Length == 0)
        {
            return false;
        }

        var partes = texto.Split('.');
        if (partes.Length == 0 || partes.Length > 4)
        {
            return false;
        }

        var numeros = new int[4];
        for (var i = 0; i < partes.Length; i++)
        {
            if (!int.TryParse(partes[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                return false;
            }

            numeros[i] = n;
        }

        version = new Version(numeros[0], numeros[1], numeros[2], numeros[3]);
        return true;
    }

    public static bool EsMasNueva(Version remota, Version actual) => remota > actual;
}
