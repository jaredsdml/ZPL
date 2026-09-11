using System.Net.Http.Headers;
using System.Text.Json;

namespace Zps.Updater;

/// <summary>
/// Consulta la API pública de GitHub Releases para saber si hay una versión más nueva
/// publicada. Cualquier problema (sin internet, timeout, GitHub caído, respuesta vacía o
/// mal formada) se captura por completo y se traduce en ReleaseCheckResult.Fallo — nunca
/// lanza una excepción hacia el llamador, para que el chequeo de actualización jamás
/// bloquee ni retrase el arranque normal de la aplicación.
/// </summary>
public sealed class GitHubReleaseChecker : IDisposable
{
    public const string RepositorioPropietario = "jaredsdml";
    public const string RepositorioNombre = "ZPL";

    private static readonly string UrlUltimoRelease =
        $"https://api.github.com/repos/{RepositorioPropietario}/{RepositorioNombre}/releases/latest";

    private static readonly TimeSpan TimeoutMaximo = TimeSpan.FromSeconds(5);

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeout;
    private readonly bool _debeDisponerHttpClient;

    public GitHubReleaseChecker(HttpClient? httpClient = null, TimeSpan? timeout = null)
    {
        _timeout = timeout is { } t && t > TimeSpan.Zero && t <= TimeoutMaximo ? t : TimeoutMaximo;
        _debeDisponerHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();

        // La API de GitHub rechaza (403) cualquier solicitud sin User-Agent.
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Zps-Updater", "1.0"));
        }

        if (_httpClient.DefaultRequestHeaders.Accept.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }
    }

    public async Task<ReleaseCheckResult> ObtenerUltimaVersionAsync(CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        try
        {
            using var respuesta = await _httpClient.GetAsync(UrlUltimoRelease, cts.Token).ConfigureAwait(false);
            if (!respuesta.IsSuccessStatusCode)
            {
                return ReleaseCheckResult.Fallo($"GitHub respondió {(int)respuesta.StatusCode} {respuesta.ReasonPhrase}");
            }

            var json = await respuesta.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return ParsearRespuesta(json);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ReleaseCheckResult.Fallo($"Tiempo de espera agotado ({_timeout.TotalSeconds:0.#}s) al consultar GitHub Releases.");
        }
        catch (HttpRequestException ex)
        {
            return ReleaseCheckResult.Fallo($"Error de red al consultar GitHub: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return ReleaseCheckResult.Fallo($"Respuesta de GitHub inválida: {ex.Message}");
        }
    }

    internal static ReleaseCheckResult ParsearRespuesta(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ReleaseCheckResult.Fallo("GitHub devolvió una respuesta vacía.");
        }

        JsonDocument documento;
        try
        {
            documento = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return ReleaseCheckResult.Fallo($"Respuesta de GitHub inválida: {ex.Message}");
        }

        using (documento)
        {
            var raiz = documento.RootElement;

            if (raiz.ValueKind != JsonValueKind.Object ||
                !raiz.TryGetProperty("tag_name", out var tagProp) ||
                tagProp.ValueKind != JsonValueKind.String ||
                tagProp.GetString() is not { Length: > 0 } tagName)
            {
                return ReleaseCheckResult.Fallo("La respuesta de GitHub no trae 'tag_name' (¿el repositorio no tiene releases públicas?).");
            }

            if (!SemVer.TryParse(tagName, out var version))
            {
                return ReleaseCheckResult.Fallo($"No se pudo interpretar la versión del tag '{tagName}'.");
            }

            string? zipUrl = null;
            string? zipNombre = null;
            string? checksumUrl = null;

            if (raiz.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out var nombreProp) || nombreProp.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var nombre = nombreProp.GetString();
                    if (nombre is null || !asset.TryGetProperty("browser_download_url", out var urlProp))
                    {
                        continue;
                    }

                    var url = urlProp.GetString();

                    if (zipUrl is null && nombre.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        zipUrl = url;
                        zipNombre = nombre;
                    }
                    else if (checksumUrl is null && nombre.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                    {
                        checksumUrl = url;
                    }
                }
            }

            var notas = raiz.TryGetProperty("body", out var bodyProp) && bodyProp.ValueKind == JsonValueKind.String
                ? bodyProp.GetString()
                : null;

            return ReleaseCheckResult.Ok(new ReleaseInfo(tagName, version, zipUrl, zipNombre, checksumUrl, notas));
        }
    }

    public void Dispose()
    {
        if (_debeDisponerHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
