namespace Zps.Hardware;

/// <summary>
/// Vista previa de etiquetas ZPL renderizadas por la API pública de Labelary, con timeout
/// estricto (por defecto y como tope máximo, 4s) y captura total de excepciones: un fallo
/// de red o un timeout siempre se traduce en ResultadoPreview.Fallo, nunca en una excepción
/// que pueda tumbar la UI. Espejo reforzado de cargar_preview_automatico (app_centralizada.py).
/// </summary>
public sealed class LabelaryPreviewService : IDisposable
{
    private static readonly TimeSpan TimeoutMaximo = TimeSpan.FromSeconds(4);

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeout;
    private readonly bool _debeDisponerHttpClient;

    public LabelaryPreviewService(HttpClient? httpClient = null, TimeSpan? timeout = null)
    {
        _timeout = timeout is { } t && t > TimeSpan.Zero && t <= TimeoutMaximo ? t : TimeoutMaximo;
        _debeDisponerHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<ResultadoPreview> GenerarVistaPreviaAsync(
        string zpl,
        string densidad = "8dpmm",
        string tamanoEtiqueta = "4x6",
        int rotacion = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(zpl))
        {
            return ResultadoPreview.Fallo("El ZPL a previsualizar está vacío.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        try
        {
            var url = LabelaryRequestBuilder.ConstruirUrl(densidad, tamanoEtiqueta, rotacion);
            using var contenido = LabelaryRequestBuilder.ConstruirContenido(zpl);
            using var respuesta = await _httpClient.PostAsync(url, contenido, cts.Token).ConfigureAwait(false);

            if (!respuesta.IsSuccessStatusCode)
            {
                return ResultadoPreview.Fallo($"Labelary respondió {(int)respuesta.StatusCode} {respuesta.ReasonPhrase}");
            }

            var bytes = await respuesta.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            return ResultadoPreview.Ok(bytes);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ResultadoPreview.Fallo($"Tiempo de espera agotado ({_timeout.TotalSeconds:0.#}s) al contactar Labelary.");
        }
        catch (HttpRequestException ex)
        {
            return ResultadoPreview.Fallo($"Error de red al contactar Labelary: {ex.Message}");
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
