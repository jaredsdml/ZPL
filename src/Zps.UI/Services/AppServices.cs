using Npgsql;
using Zps.Data;
using Zps.Data.LocalCache;
using Zps.Hardware;

namespace Zps.UI.Services;

/// <summary>
/// Composición de servicios de toda la app (equivalente a una raíz de inyección de
/// dependencias, hecha a mano para mantener el shell simple). Neon es opcional en tiempo
/// de construcción: si no se pudo resolver el connection string, los servicios de Neon
/// quedan en null y la app arranca igual, en modo degradado (solo caché local).
/// </summary>
public sealed class AppServices : IAsyncDisposable
{
    public required NpgsqlDataSource? NeonDataSource { get; init; }
    public required ConsecutivosService? Consecutivos { get; init; }
    public required HistoricoService? Historico { get; init; }
    public required CatalogoClientesService? CatalogoClientes { get; init; }
    public required PlantillasService? Plantillas { get; init; }
    public required AadWmsService? Wms { get; init; }
    public required LocalCacheStore CacheLocal { get; init; }
    public required PrinterService Impresoras { get; init; }
    public required LabelaryPreviewService Preview { get; init; }
    public string? ErrorConfiguracionNeon { get; init; }
    public string? ErrorConfiguracionWms { get; init; }

    public bool NeonDisponible => NeonDataSource is not null;
    public bool WmsDisponible => Wms is not null;

    public async ValueTask DisposeAsync()
    {
        if (NeonDataSource is not null)
        {
            await NeonDataSource.DisposeAsync();
        }

        await Impresoras.DisposeAsync();
        Preview.Dispose();
    }
}
