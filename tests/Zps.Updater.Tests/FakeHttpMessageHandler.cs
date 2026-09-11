namespace Zps.Updater.Tests;

/// <summary>Intercepta la solicitud HTTP antes de que salga a la red real, para simular respuestas de GitHub de forma determinista.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public HttpRequestMessage? UltimaSolicitud { get; private set; }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        UltimaSolicitud = request;
        return await _responder(request, cancellationToken);
    }
}
