using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Zps.Updater;

/// <summary>
/// Orquesta el flujo completo de actualización sin dañar la base local:
///   1) Descarga el .zip del asset a %TEMP%\ZPS_Update.
///   2) Valida su checksum SHA-256 si la release publicó uno (asset .sha256 hermano);
///      si no hay checksum disponible, continúa sin validar (queda registrado en el resultado).
///   3) Extrae el paquete a una subcarpeta de staging, separada de la carpeta final.
///   4) Genera y lanza un script auxiliar (.bat) que espera a que el proceso principal
///      cierre, copia los archivos sobre la carpeta de instalación y relanza la app.
///
/// El .bat solo escribe sobre la carpeta de instalación que se le indica explícitamente;
/// nunca toca %LOCALAPPDATA%\LogAm\ZPS\ (la caché SQLite vive en un árbol de directorios
/// completamente distinto, así que no hay overlap posible).
/// </summary>
public sealed class AtomicUpdateInstaller : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _debeDisponerHttpClient;

    public string CarpetaTemporal { get; }

    public AtomicUpdateInstaller(HttpClient? httpClient = null, string? carpetaTemporal = null)
    {
        _debeDisponerHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        CarpetaTemporal = carpetaTemporal ?? Path.Combine(Path.GetTempPath(), "ZPS_Update");
    }

    /// <summary>
    /// Descarga el asset .zip de la release a una carpeta temporal limpia (cualquier
    /// residuo de una actualización anterior fallida se borra primero) y devuelve la ruta
    /// del archivo descargado.
    /// </summary>
    public async Task<string> DescargarPaqueteAsync(ReleaseInfo release, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(release.ZipAssetUrl))
        {
            throw new InvalidOperationException("La release no tiene un asset .zip para descargar.");
        }

        if (Directory.Exists(CarpetaTemporal))
        {
            Directory.Delete(CarpetaTemporal, recursive: true);
        }

        Directory.CreateDirectory(CarpetaTemporal);

        var nombreArchivo = string.IsNullOrWhiteSpace(release.ZipAssetNombre) ? "actualizacion.zip" : release.ZipAssetNombre;
        var rutaZip = Path.Combine(CarpetaTemporal, nombreArchivo);

        using var respuesta = await _httpClient.GetAsync(release.ZipAssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        respuesta.EnsureSuccessStatusCode();

        await using (var flujoOrigen = await respuesta.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var flujoDestino = File.Create(rutaZip))
        {
            await flujoOrigen.CopyToAsync(flujoDestino, cancellationToken).ConfigureAwait(false);
        }

        return rutaZip;
    }

    /// <summary>
    /// Si la release publicó un asset .sha256 hermano, lo descarga y compara contra el
    /// hash real del archivo. Devuelve true si coincide o si no había checksum publicado
    /// (nada que validar); false solo si había un checksum y no coincidió.
    /// </summary>
    public async Task<bool> ValidarIntegridadAsync(string rutaZip, ReleaseInfo release, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(release.ChecksumAssetUrl))
        {
            return true;
        }

        var contenidoChecksum = await _httpClient.GetStringAsync(release.ChecksumAssetUrl, cancellationToken).ConfigureAwait(false);
        var hashEsperado = ExtraerHashDeArchivoChecksum(contenidoChecksum);
        if (hashEsperado is null)
        {
            return true;
        }

        var hashReal = await CalcularSha256Async(rutaZip, cancellationToken).ConfigureAwait(false);
        return string.Equals(hashEsperado, hashReal, StringComparison.OrdinalIgnoreCase);
    }

    internal static string? ExtraerHashDeArchivoChecksum(string contenido)
    {
        // Formato estándar de sha256sum: "<hash en hex>  <nombre de archivo>"
        var primeraLinea = contenido
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(primeraLinea))
        {
            return null;
        }

        var hash = primeraLinea.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrEmpty(hash) ? null : hash;
    }

    private static async Task<string> CalcularSha256Async(string rutaArchivo, CancellationToken cancellationToken)
    {
        await using var flujo = File.OpenRead(rutaArchivo);
        var bytesHash = await SHA256.HashDataAsync(flujo, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(bytesHash);
    }

    /// <summary>Extrae el paquete descargado a una subcarpeta de staging, separada del zip original.</summary>
    public string ExtraerPaquete(string rutaZip)
    {
        var carpetaStaging = Path.Combine(CarpetaTemporal, "staged");
        if (Directory.Exists(carpetaStaging))
        {
            Directory.Delete(carpetaStaging, recursive: true);
        }

        ZipFile.ExtractToDirectory(rutaZip, carpetaStaging);
        return carpetaStaging;
    }

    /// <summary>
    /// Genera el script auxiliar y lo lanza como proceso independiente. El llamador es
    /// responsable de cerrar la aplicación inmediatamente después (el script espera a que
    /// el PID indicado termine antes de copiar los archivos).
    /// </summary>
    public Process LanzarScriptDeReemplazo(string carpetaInstalacion, string carpetaStaging, string nombreEjecutable)
    {
        var rutaScript = Path.Combine(CarpetaTemporal, "updater.bat");
        var contenido = UpdaterScriptTemplate.Generar(
            Environment.ProcessId,
            carpetaInstalacion,
            carpetaStaging,
            nombreEjecutable);

        File.WriteAllText(rutaScript, contenido);

        var info = new ProcessStartInfo
        {
            FileName = rutaScript,
            WorkingDirectory = CarpetaTemporal,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        return Process.Start(info) ?? throw new InvalidOperationException("No se pudo iniciar el script auxiliar de actualización.");
    }

    /// <summary>
    /// Flujo completo: descarga, valida integridad, extrae y lanza el script de reemplazo.
    /// No cierra la aplicación: eso queda a cargo del llamador una vez que este método
    /// retorna exitosamente.
    /// </summary>
    public async Task PrepararActualizacionAsync(
        ReleaseInfo release,
        string carpetaInstalacion,
        string nombreEjecutable,
        CancellationToken cancellationToken = default)
    {
        var rutaZip = await DescargarPaqueteAsync(release, cancellationToken).ConfigureAwait(false);

        var integro = await ValidarIntegridadAsync(rutaZip, release, cancellationToken).ConfigureAwait(false);
        if (!integro)
        {
            throw new UpdateIntegrityException(
                $"El checksum del paquete descargado no coincide con el publicado para la versión {release.TagName}. Actualización cancelada.");
        }

        var carpetaStaging = ExtraerPaquete(rutaZip);
        LanzarScriptDeReemplazo(carpetaInstalacion, carpetaStaging, nombreEjecutable);
    }

    public void Dispose()
    {
        if (_debeDisponerHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
