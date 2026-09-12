using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zps.Data.Models;
using Zps.UI.Services;

namespace Zps.UI.ViewModels;

/// <summary>
/// ViewModel de la ventana de administración de plantillas ZPL (plantillas_zpl): permite dar
/// de alta, editar y eliminar la plantilla enlazada a una hoja de Excel sin publicar un
/// release. Escribe en Neon y replica en la caché local (SQLite) para que el Generador
/// Principal pueda resolver la plantilla dinámica aunque la estación esté sin conexión.
/// </summary>
public sealed partial class AdminPlantillasViewModel : ObservableObject
{
    private readonly AppServices _services;

    public ObservableCollection<PlantillaZplRecord> Plantillas { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EliminarCommand))]
    private PlantillaZplRecord? _plantillaSeleccionada;

    [ObservableProperty]
    private string _hojaExcel = string.Empty;

    [ObservableProperty]
    private string _nombreCliente = string.Empty;

    [ObservableProperty]
    private string _codigoZpl = string.Empty;

    [ObservableProperty]
    private bool _activo = true;

    [ObservableProperty]
    private string _estadoMensaje = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    [NotifyCanExecuteChangedFor(nameof(EliminarCommand))]
    private bool _estaOcupado;

    public AdminPlantillasViewModel(AppServices services)
    {
        _services = services;
        _ = CargarAsync();
    }

    private async Task CargarAsync()
    {
        EstaOcupado = true;
        try
        {
            IReadOnlyList<PlantillaZplRecord> plantillas;
            if (_services.Plantillas is not null)
            {
                try
                {
                    plantillas = await _services.Plantillas.ObtenerTodasAsync();

                    // plantillas_zpl vacía: siembra automáticamente desde las plantillas
                    // legacy ya activas en cat_clientes (MYC, AXO, MNS/MINISO, PRM, CEVA,
                    // DRL, etc.), para que el panel no arranque vacío en el primer uso.
                    if (plantillas.Count == 0)
                    {
                        var migradas = await _services.Plantillas.MigrarPlantillasLegacySiVacioAsync();
                        if (migradas.Count > 0)
                        {
                            await _services.CacheLocal.ReplicarPlantillasAsync(migradas);
                            plantillas = await _services.Plantillas.ObtenerTodasAsync();
                        }
                    }

                    await _services.CacheLocal.ReplicarPlantillasAsync(plantillas);
                    EstadoMensaje = $"{plantillas.Count} plantilla(s) cargadas desde Neon.";
                }
                catch (Exception ex)
                {
                    EstadoMensaje = $"Neon no disponible ({ex.Message}); mostrando caché local.";
                    plantillas = await _services.CacheLocal.ObtenerPlantillasAsync();
                }
            }
            else
            {
                plantillas = await _services.CacheLocal.ObtenerPlantillasAsync();
                EstadoMensaje = $"{plantillas.Count} plantilla(s) desde caché local (sin conexión a Neon).";
            }

            Plantillas.Clear();
            foreach (var plantilla in plantillas)
            {
                Plantillas.Add(plantilla);
            }
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    /// <summary>
    /// Botón "Restaurar / Importar Plantillas Base": fuerza la reimportación desde
    /// cat_clientes aunque plantillas_zpl ya tenga filas, sobrescribiendo por hoja_excel
    /// cualquier plantilla existente con el mismo nombre — por eso pide confirmación antes
    /// de tocar nada.
    /// </summary>
    [RelayCommand]
    private async Task RestaurarPlantillasBaseAsync()
    {
        if (_services.Plantillas is null)
        {
            EstadoMensaje = "Neon no está disponible: no se puede importar desde cat_clientes sin conexión.";
            return;
        }

        var confirmar = MessageBox.Show(
            "Esto reimporta las plantillas ZPL originales (MYC, AXO, MNS/MINISO, PRM, CEVA, DRL, etc.) desde cat_clientes, " +
            "sobrescribiendo cualquier plantilla ya guardada en plantillas_zpl que tenga el mismo nombre de hoja. ¿Continuar?",
            "Restaurar plantillas base",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmar != MessageBoxResult.Yes)
        {
            return;
        }

        EstaOcupado = true;
        try
        {
            var migradas = await _services.Plantillas.MigrarPlantillasLegacySiVacioAsync(forzar: true);
            if (migradas.Count > 0)
            {
                await _services.CacheLocal.ReplicarPlantillasAsync(migradas);
            }

            await CargarAsync();
            EstadoMensaje = $"Se importaron/actualizaron {migradas.Count} plantilla(s) base desde cat_clientes.";
        }
        catch (Exception ex)
        {
            EstadoMensaje = $"No se pudo importar: {ex.Message}";
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    partial void OnPlantillaSeleccionadaChanged(PlantillaZplRecord? value)
    {
        if (value is null)
        {
            return;
        }

        HojaExcel = value.HojaExcel;
        NombreCliente = value.NombreCliente;
        CodigoZpl = value.CodigoZpl;
        Activo = value.Activo;
    }

    [RelayCommand]
    private void NuevaPlantilla()
    {
        PlantillaSeleccionada = null;
        HojaExcel = string.Empty;
        NombreCliente = string.Empty;
        CodigoZpl = string.Empty;
        Activo = true;
        EstadoMensaje = "Nueva plantilla: completa Hoja de Excel, Cliente y código ZPL, y guarda.";
    }

    private bool PuedeGuardar() => !EstaOcupado;

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        var hoja = HojaExcel.Trim();
        var nombre = NombreCliente.Trim();

        if (string.IsNullOrWhiteSpace(hoja) || string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(CodigoZpl))
        {
            EstadoMensaje = "Hoja de Excel, Nombre de Cliente y Código ZPL son obligatorios.";
            return;
        }

        EstaOcupado = true;
        try
        {
            var plantilla = new PlantillaZplRecord(
                Id: PlantillaSeleccionada?.Id,
                HojaExcel: hoja,
                NombreCliente: nombre,
                CodigoZpl: CodigoZpl,
                Activo: Activo,
                FechaModificacion: DateTimeOffset.UtcNow);

            if (_services.Plantillas is not null)
            {
                try
                {
                    await _services.Plantillas.GuardarOActualizarAsync(plantilla);
                }
                catch (Exception ex)
                {
                    EstadoMensaje = $"No se pudo guardar en Neon ({ex.Message}); se guardó solo en caché local.";
                }
            }

            await _services.CacheLocal.ReplicarPlantillasAsync(new[] { plantilla });

            await CargarAsync();
            PlantillaSeleccionada = Plantillas.FirstOrDefault(p =>
                string.Equals(p.HojaExcel, hoja, StringComparison.OrdinalIgnoreCase));
            EstadoMensaje = $"Plantilla '{hoja}' guardada correctamente.";
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    private bool PuedeEliminar() => PlantillaSeleccionada is not null && !EstaOcupado;

    [RelayCommand(CanExecute = nameof(PuedeEliminar))]
    private async Task EliminarAsync()
    {
        if (PlantillaSeleccionada is null)
        {
            return;
        }

        var hoja = PlantillaSeleccionada.HojaExcel;

        EstaOcupado = true;
        try
        {
            if (_services.Plantillas is not null)
            {
                try
                {
                    await _services.Plantillas.EliminarAsync(hoja);
                }
                catch (Exception ex)
                {
                    EstadoMensaje = $"No se pudo eliminar en Neon ({ex.Message}); se eliminó solo de la caché local.";
                }
            }

            await _services.CacheLocal.EliminarPlantillaAsync(hoja);

            var registro = Plantillas.FirstOrDefault(p => string.Equals(p.HojaExcel, hoja, StringComparison.OrdinalIgnoreCase));
            if (registro is not null)
            {
                Plantillas.Remove(registro);
            }

            NuevaPlantilla();
            EstadoMensaje = $"Plantilla '{hoja}' eliminada.";
        }
        finally
        {
            EstaOcupado = false;
        }
    }
}
