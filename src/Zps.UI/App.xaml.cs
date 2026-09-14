using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Zps.Data;
using Zps.Data.LocalCache;
using Zps.Hardware;
using Zps.UI.Services;
using Zps.UI.ViewModels;
using Zps.Updater;

namespace Zps.UI;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Npgsql.NpgsqlDataSource? dataSource = null;
        ConsecutivosService? consecutivos = null;
        HistoricoService? historico = null;
        CatalogoClientesService? catalogo = null;
        PlantillasService? plantillas = null;
        string? errorNeon = null;

        try
        {
            var connectionString = NeonConnectionStringResolver.Resolve();
            dataSource = NeonDataSourceFactory.Create(connectionString);
            consecutivos = new ConsecutivosService(dataSource);
            historico = new HistoricoService(dataSource);
            catalogo = new CatalogoClientesService(dataSource);
            plantillas = new PlantillasService(dataSource);
        }
        catch (Exception ex)
        {
            errorNeon = ex.Message;
        }

        AadWmsService? wms = null;
        string? errorWms = null;
        try
        {
            wms = new AadWmsService(AadConnectionStringResolver.Resolve());
        }
        catch (Exception ex)
        {
            errorWms = ex.Message;
        }

        var cacheLocal = new LocalCacheStore();
        try
        {
            await cacheLocal.InicializarAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se pudo inicializar la caché local (SQLite):\n{ex.Message}\n\nLa app continuará, pero sin caché offline.",
                "Zps - Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Services = new AppServices
        {
            NeonDataSource = dataSource,
            Consecutivos = consecutivos,
            Historico = historico,
            CatalogoClientes = catalogo,
            Plantillas = plantillas,
            Wms = wms,
            CacheLocal = cacheLocal,
            Impresoras = new PrinterService(),
            Preview = new LabelaryPreviewService(),
            ErrorConfiguracionNeon = errorNeon,
            ErrorConfiguracionWms = errorWms,
        };

        ThemeManager.Aplicar(oscuro: false);

        var mainViewModel = new MainViewModel(Services);
        var mainWindow = new MainWindow { DataContext = mainViewModel };
        MainWindow = mainWindow;
        mainWindow.Show();

        try
        {
            await mainViewModel.InicializarAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error inicializando la ventana principal:\n{ex.Message}",
                "Zps - Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // Chequeo de actualización en segundo plano: nunca bloquea ni retrasa el arranque,
        // y un fallo (sin internet, GitHub caído, timeout) se ignora en silencio.
        _ = Task.Run(VerificarActualizacionAsync);
    }

    private static async Task VerificarActualizacionAsync()
    {
        try
        {
            using var checker = new GitHubReleaseChecker();
            var resultado = await checker.ObtenerUltimaVersionAsync();
            if (!resultado.Exito || resultado.Release is null)
            {
                return;
            }

            var versionActual = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
            if (!SemVer.EsMasNueva(resultado.Release.Version, versionActual))
            {
                return;
            }

            var release = resultado.Release;
            await Current.Dispatcher.InvokeAsync(async () =>
            {
                var respuesta = MessageBox.Show(
                    $"Nueva versión {release.TagName} disponible. ¿Deseas actualizar ahora?",
                    "Zebra Print Station - Actualización disponible",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (respuesta == MessageBoxResult.Yes)
                {
                    await IniciarActualizacionAsync(release);
                }
            });
        }
        catch
        {
            // Silencioso a propósito: el chequeo de actualización jamás debe interrumpir la operativa.
        }
    }

    private static async Task IniciarActualizacionAsync(ReleaseInfo release)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(release.ZipAssetUrl))
            {
                MessageBox.Show(
                    "La nueva versión no publicó un paquete .zip descargable; no se puede actualizar automáticamente.",
                    "Zps - Actualización", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using var instalador = new AtomicUpdateInstaller();
            var carpetaInstalacion = AppContext.BaseDirectory;
            var nombreEjecutable = Path.GetFileName(Process.GetCurrentProcess().MainModule?.FileName ?? "Zps.UI.exe");

            await instalador.PrepararActualizacionAsync(release, carpetaInstalacion, nombreEjecutable);

            // El script auxiliar espera a que este proceso cierre antes de reemplazar archivos.
            Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se pudo completar la actualización:\n{ex.Message}",
                "Zps - Error de actualización", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (Services is not null)
        {
            await Services.DisposeAsync();
        }

        base.OnExit(e);
    }
}
