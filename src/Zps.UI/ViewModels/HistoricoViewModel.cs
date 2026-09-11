using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zps.Core;
using Zps.Data.Models;
using Zps.UI.Services;
using Zps.UI.Views;

namespace Zps.UI.ViewModels;

/// <summary>
/// ViewModel de la pestaña "Histórico y Reimpresiones": busca en Neon (con respaldo a la
/// caché local si no hay conexión) y reimprime folios ya existentes reconstruyendo el ZPL
/// a partir de la plantilla actual del cliente + los datos de fila guardados, sin llamar
/// nunca a ConsecutivosService (no se generan ni alteran folios al reimprimir).
///
/// La búsqueda es de dos niveles: Sincronizar trae el lote desde Neon/caché local (con el
/// mismo texto ya aplicado como filtro SQL, para no descargar de más), y BusquedaTexto
/// además filtra en vivo, en memoria, sobre ese lote ya descargado (ICollectionView) —
/// así cada tecla resulta instantánea, sin ida y vuelta a la red.
/// </summary>
public sealed partial class HistoricoViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly GeneradorPrincipalViewModel _generador;
    private readonly ICollectionView _vistaFiltrada;

    public ObservableCollection<HistoricoImpresionRecord> Registros { get; } = new();

    /// <summary>Vista filtrada en vivo sobre Registros: esto es lo que debe enlazar el DataGrid.</summary>
    public ICollectionView VistaFiltrada => _vistaFiltrada;

    [ObservableProperty]
    private string _busquedaTexto = string.Empty;

    [ObservableProperty]
    private string _estadoMensaje = "Sin sincronizar todavía.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportarExcelCommand))]
    [NotifyCanExecuteChangedFor(nameof(EliminarSeleccionCommand))]
    private bool _estaOcupado;

    private IReadOnlyList<HistoricoImpresionRecord> _seleccionados = Array.Empty<HistoricoImpresionRecord>();

    public HistoricoViewModel(AppServices services, GeneradorPrincipalViewModel generador)
    {
        _services = services;
        _generador = generador;

        _vistaFiltrada = CollectionViewSource.GetDefaultView(Registros);
        _vistaFiltrada.Filter = FiltrarRegistro;
    }

    /// <summary>Llamado desde el code-behind de la vista cuando cambia la selección del DataGrid.</summary>
    public void ActualizarSeleccion(IReadOnlyList<HistoricoImpresionRecord> seleccionados)
    {
        _seleccionados = seleccionados;
        ReimprimirSeleccionCommand.NotifyCanExecuteChanged();
        EliminarSeleccionCommand.NotifyCanExecuteChanged();
    }

    partial void OnBusquedaTextoChanged(string value)
    {
        // Filtrado instantáneo en memoria: no dispara ninguna consulta nueva.
        _vistaFiltrada.Refresh();
        ExportarExcelCommand.NotifyCanExecuteChanged();
    }

    private bool FiltrarRegistro(object obj)
    {
        if (obj is not HistoricoImpresionRecord registro)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(BusquedaTexto))
        {
            return true;
        }

        var texto = BusquedaTexto.Trim();
        return Coincide(registro.Lpn, texto)
            || Coincide(registro.Arribo, texto)
            || Coincide(registro.Cliente, texto)
            || Coincide(registro.Solicitante, texto)
            || Coincide(registro.Tipo, texto)
            || Coincide(registro.Sku, texto)
            || Coincide(registro.Lote, texto);
    }

    private static bool Coincide(string? valor, string texto) =>
        !string.IsNullOrEmpty(valor) && valor.Contains(texto, StringComparison.OrdinalIgnoreCase);

    private List<HistoricoImpresionRecord> ObtenerFilasVisibles() =>
        _vistaFiltrada.Cast<HistoricoImpresionRecord>().ToList();

    [RelayCommand]
    private async Task SincronizarAsync()
    {
        EstaOcupado = true;
        try
        {
            IReadOnlyList<HistoricoImpresionRecord> resultados;

            if (_services.Historico is not null)
            {
                try
                {
                    resultados = await _services.Historico.BuscarAsync(BusquedaTexto);
                    await _services.CacheLocal.ReplicarHistoricoAsync(resultados);
                    EstadoMensaje = $"{resultados.Count} registro(s) sincronizados desde Neon.";
                }
                catch (Exception ex)
                {
                    EstadoMensaje = $"Neon no disponible ({ex.Message}); mostrando caché local.";
                    resultados = await _services.CacheLocal.BuscarHistoricoAsync(BusquedaTexto);
                }
            }
            else
            {
                resultados = await _services.CacheLocal.BuscarHistoricoAsync(BusquedaTexto);
                EstadoMensaje = $"{resultados.Count} registro(s) desde caché local (sin conexión a Neon).";
            }

            Registros.Clear();
            foreach (var registro in resultados)
            {
                Registros.Add(registro);
            }
        }
        finally
        {
            EstaOcupado = false;
            ExportarExcelCommand.NotifyCanExecuteChanged();
        }
    }

    private bool PuedeReimprimir() => _seleccionados.Count > 0 && !EstaOcupado;

    [RelayCommand(CanExecute = nameof(PuedeReimprimir))]
    private async Task ReimprimirSeleccionAsync()
    {
        var impresora = _generador.ImpresoraSeleccionada;
        if (string.IsNullOrWhiteSpace(impresora))
        {
            EstadoMensaje = "Selecciona una impresora en la pestaña Generador Principal antes de reimprimir.";
            return;
        }

        EstaOcupado = true;
        try
        {
            var total = _seleccionados.Count;
            var errores = 0;

            for (var i = 0; i < _seleccionados.Count; i++)
            {
                var registro = _seleccionados[i];
                var cliente = _generador.Clientes.FirstOrDefault(c =>
                    string.Equals(c.Codigo, registro.Cliente, StringComparison.OrdinalIgnoreCase));

                if (cliente?.PlantillaZpl is null)
                {
                    errores++;
                    continue;
                }

                var datos = string.IsNullOrEmpty(registro.VariablesJson)
                    ? new Dictionary<string, string?>()
                    : JsonSerializer.Deserialize<Dictionary<string, string?>>(registro.VariablesJson) ?? new Dictionary<string, string?>();

                // El LPN reimpreso siempre debe ser el folio real de este registro. SKU,
                // LOTE y CANTIDAD normalmente ya vienen en variables_json (fila completa
                // original); se rellenan aquí solo si faltaran, sin pisar el valor correcto
                // ya presente, para que la etiqueta reimpresa salga idéntica a la original.
                datos["LPN"] = registro.Lpn;
                if (registro.Sku is not null)
                {
                    datos.TryAdd("SKU", registro.Sku);
                }

                if (registro.Lote is not null)
                {
                    datos.TryAdd("LOTE", registro.Lote);
                }

                if (registro.Cantidad.HasValue)
                {
                    var cantidadTexto = registro.Cantidad.Value.ToString(CultureInfo.InvariantCulture);
                    datos.TryAdd("CANTIDAD", cantidadTexto);
                    datos.TryAdd("QTY", cantidadTexto);
                }

                var zpl = ZplTemplateEngine.Generar(cliente.PlantillaZpl, cliente.MapeoColumnas, datos, i + 1, total);
                var resultado = await _services.Impresoras.EncolarAsync(impresora, zpl, $"Reimpresión {registro.Lpn}");
                if (!resultado.Exito)
                {
                    errores++;
                }
            }

            EstadoMensaje = $"Reimpresión terminada: {total} etiqueta(s), {errores} error(es). No se alteraron secuencias de folio.";
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    private bool PuedeExportar() => !EstaOcupado && ObtenerFilasVisibles().Count > 0;

    [RelayCommand(CanExecute = nameof(PuedeExportar))]
    private void ExportarExcel()
    {
        var filas = ObtenerFilasVisibles();
        if (filas.Count == 0)
        {
            EstadoMensaje = "No hay filas visibles para exportar.";
            return;
        }

        var dialogo = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Archivos Excel (*.xlsx)|*.xlsx",
            FileName = ConstruirNombreArchivoSugerido(filas)
        };

        if (dialogo.ShowDialog() != true)
        {
            return;
        }

        try
        {
            using var libro = new XLWorkbook();
            var hoja = libro.Worksheets.Add("Historico");

            string[] encabezados = { "LPN", "Consecutivo", "Tipo", "Cliente", "Solicitante", "Arribo", "SKU", "Lote", "Cantidad", "Fecha/Hora" };
            for (var c = 0; c < encabezados.Length; c++)
            {
                hoja.Cell(1, c + 1).Value = encabezados[c];
            }

            var rangoEncabezado = hoja.Range(1, 1, 1, encabezados.Length);
            rangoEncabezado.Style.Font.Bold = true;
            rangoEncabezado.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF0F3");
            rangoEncabezado.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

            for (var i = 0; i < filas.Count; i++)
            {
                var registro = filas[i];
                var fila = i + 2;

                hoja.Cell(fila, 1).Value = registro.Lpn;
                if (registro.Consecutivo.HasValue)
                {
                    hoja.Cell(fila, 2).Value = registro.Consecutivo.Value;
                }

                hoja.Cell(fila, 3).Value = registro.Tipo ?? string.Empty;
                hoja.Cell(fila, 4).Value = registro.Cliente ?? string.Empty;
                hoja.Cell(fila, 5).Value = registro.Solicitante;
                hoja.Cell(fila, 6).Value = registro.Arribo;
                hoja.Cell(fila, 7).Value = registro.Sku ?? string.Empty;
                hoja.Cell(fila, 8).Value = registro.Lote ?? string.Empty;
                if (registro.Cantidad.HasValue)
                {
                    hoja.Cell(fila, 9).Value = registro.Cantidad.Value;
                }

                var celdaFecha = hoja.Cell(fila, 10);
                celdaFecha.Value = registro.FechaHora.LocalDateTime;
                celdaFecha.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
            }

            hoja.SheetView.FreezeRows(1);
            hoja.Columns().AdjustToContents();

            libro.SaveAs(dialogo.FileName);
            EstadoMensaje = $"Se exportaron {filas.Count} registro(s) a {Path.GetFileName(dialogo.FileName)}.";
        }
        catch (Exception ex)
        {
            EstadoMensaje = $"No se pudo exportar el archivo: {ex.Message}";
        }
    }

    /// <summary>
    /// Si hay una selección o el filtro actual dejó visible un único arribo, el nombre
    /// propuesto es "[ARRIBO]_[FECHA].xlsx"; si no hay un arribo específico identificable,
    /// cae a "Historico_LPNs_[FECHA].xlsx".
    /// </summary>
    private string ConstruirNombreArchivoSugerido(IReadOnlyList<HistoricoImpresionRecord> filasVisibles)
    {
        var fecha = DateTime.Now.ToString("yyyyMMdd");
        var arribo = DeterminarArriboParaNombreArchivo(filasVisibles);

        return arribo is not null
            ? $"{SanearNombreArchivo(arribo)}_{fecha}.xlsx"
            : $"Historico_LPNs_{fecha}.xlsx";
    }

    private string? DeterminarArriboParaNombreArchivo(IReadOnlyList<HistoricoImpresionRecord> filasVisibles)
    {
        if (_seleccionados.Count > 0)
        {
            var arribosSeleccionados = _seleccionados
                .Select(r => r.Arribo)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (arribosSeleccionados.Count == 1)
            {
                return arribosSeleccionados[0];
            }
        }

        var arribosVisibles = filasVisibles
            .Select(r => r.Arribo)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return arribosVisibles.Count == 1 ? arribosVisibles[0] : null;
    }

    private static string SanearNombreArchivo(string nombre)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var limpio = new string(nombre.Select(c => invalidos.Contains(c) ? '_' : c).ToArray());
        return limpio.Trim();
    }

    private bool PuedeEliminar() => _seleccionados.Count > 0 && !EstaOcupado;

    /// <summary>
    /// Borrado seguro: exige confirmar con contraseña antes de eliminar permanentemente
    /// los registros seleccionados de Neon y de la caché local. Un intento de contraseña
    /// incorrecto cancela toda la operación (sin reintentos).
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeEliminar))]
    private async Task EliminarSeleccionAsync()
    {
        if (_seleccionados.Count == 0)
        {
            return;
        }

        var dialogo = new ConfirmacionPasswordWindow { Owner = Application.Current.MainWindow };
        if (dialogo.ShowDialog() != true)
        {
            return; // cancelado o contraseña incorrecta: no se elimina nada
        }

        EstaOcupado = true;
        try
        {
            var lpns = _seleccionados.Select(r => r.Lpn).ToList();

            if (_services.Historico is not null)
            {
                await _services.Historico.EliminarAsync(lpns);
            }

            await _services.CacheLocal.EliminarHistoricoAsync(lpns);

            foreach (var lpn in lpns)
            {
                var registro = Registros.FirstOrDefault(r => r.Lpn == lpn);
                if (registro is not null)
                {
                    Registros.Remove(registro);
                }
            }

            _seleccionados = Array.Empty<HistoricoImpresionRecord>();
            EstadoMensaje = $"Se eliminaron {lpns.Count} registro(s) de Neon y de la caché local.";
        }
        catch (Exception ex)
        {
            EstadoMensaje = $"No se pudo completar la eliminación: {ex.Message}";
        }
        finally
        {
            EstaOcupado = false;
            ReimprimirSeleccionCommand.NotifyCanExecuteChanged();
            EliminarSeleccionCommand.NotifyCanExecuteChanged();
            ExportarExcelCommand.NotifyCanExecuteChanged();
        }
    }
}
