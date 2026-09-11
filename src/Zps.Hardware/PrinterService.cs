using System.Runtime.Versioning;
using System.Threading.Channels;

namespace Zps.Hardware;

/// <summary>
/// Descubrimiento + validación + envío asíncrono y no bloqueante de trabajos de impresión.
///
/// EncolarAsync valida que la impresora exista en el spooler ANTES de despachar cualquier
/// byte (evita abrir un handle contra un nombre inexistente), encola el trabajo en un
/// Channel&lt;T&gt; y devuelve de inmediato una Task que se completa cuando un único
/// procesador de fondo (Task.Run) termina de enviarlo — el hilo que llama a EncolarAsync
/// nunca se bloquea ni se congela esperando al spooler.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PrinterService : IAsyncDisposable, IDisposable
{
    private readonly Func<IReadOnlyList<string>> _listarImpresoras;
    private readonly Action<string, string, string> _enviarRaw;
    private readonly Channel<TrabajoImpresion> _cola = Channel.CreateUnbounded<TrabajoImpresion>();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _bucleProcesador;

    public PrinterService() : this(PrinterDiscovery.ListarInstaladas, RawPrinter.EnviarRaw)
    {
    }

    /// <summary>
    /// Punto de extensión para pruebas: permite inyectar el descubrimiento de impresoras
    /// y el envío RAW real sin depender del spooler físico de Windows.
    /// </summary>
    internal PrinterService(Func<IReadOnlyList<string>> listarImpresoras, Action<string, string, string> enviarRaw)
    {
        _listarImpresoras = listarImpresoras;
        _enviarRaw = enviarRaw;
        _bucleProcesador = Task.Run(ProcesarColaAsync);
    }

    public IReadOnlyList<string> ListarImpresorasInstaladas() => _listarImpresoras();

    public bool ExisteImpresora(string? nombreImpresora)
    {
        if (string.IsNullOrWhiteSpace(nombreImpresora))
        {
            return false;
        }

        return ListarImpresorasInstaladas().Any(n => string.Equals(n, nombreImpresora, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Encola un trabajo de impresión. Devuelve inmediatamente; la Task devuelta se resuelve
    /// cuando el envío realmente ocurre en el hilo de fondo (éxito o fallo, nunca lanza).
    /// </summary>
    public Task<ResultadoImpresion> EncolarAsync(string nombreImpresora, string zpl, string nombreTrabajo = "Etiqueta ZPL")
    {
        if (!ExisteImpresora(nombreImpresora))
        {
            return Task.FromResult(ResultadoImpresion.Fallo(
                $"La impresora '{nombreImpresora}' no existe en el spooler de Windows."));
        }

        var tcs = new TaskCompletionSource<ResultadoImpresion>(TaskCreationOptions.RunContinuationsAsynchronously);
        var trabajo = new TrabajoImpresion(nombreImpresora, zpl, nombreTrabajo, tcs);

        if (!_cola.Writer.TryWrite(trabajo))
        {
            tcs.SetResult(ResultadoImpresion.Fallo("No se pudo encolar el trabajo de impresión: la cola está cerrada."));
        }

        return tcs.Task;
    }

    private async Task ProcesarColaAsync()
    {
        try
        {
            await foreach (var trabajo in _cola.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    _enviarRaw(trabajo.NombreImpresora, trabajo.Zpl, trabajo.NombreTrabajo);
                    trabajo.Tcs.TrySetResult(ResultadoImpresion.Ok());
                }
                catch (Exception ex)
                {
                    trabajo.Tcs.TrySetResult(ResultadoImpresion.Fallo(ex.Message));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado normal vía DisposeAsync.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cola.Writer.TryComplete();
        _cts.Cancel();
        try
        {
            await _bucleProcesador;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private sealed record TrabajoImpresion(
        string NombreImpresora,
        string Zpl,
        string NombreTrabajo,
        TaskCompletionSource<ResultadoImpresion> Tcs);
}
