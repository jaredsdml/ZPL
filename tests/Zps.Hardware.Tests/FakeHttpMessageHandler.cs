namespace Zps.Hardware.Tests;

/// <summary>
/// Handler HTTP de prueba: intercepta la solicitud antes de que salga a la red real,
/// permitiendo simular respuestas de Labelary (éxito, error, lento/timeout) de forma
/// determinista y sin depender de internet.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public HttpRequestMessage? UltimaSolicitud { get; private set; }
    public string? UltimoContenidoEnviado { get; private set; }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        UltimaSolicitud = request;
        if (request.Content is not null)
        {
            UltimoContenidoEnviado = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return await _responder(request, cancellationToken);
    }
}
