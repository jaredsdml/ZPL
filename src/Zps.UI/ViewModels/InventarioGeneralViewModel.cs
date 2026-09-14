using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.SqlClient;
using Zps.Data.Models.Wms;
using Zps.UI.Services;

namespace Zps.UI.ViewModels;

/// <summary>Una opción del selector de tope de resultados (ver <see cref="InventarioGeneralViewModel.OpcionesLimite"/>).</summary>
public sealed record OpcionLimiteInventario(string Etiqueta, int? Valor);

/// <summary>
/// Un valor único de una columna dentro del filtro estilo Excel de su encabezado (ver
/// InventarioGeneralViewModel.OpcionesFiltro*). Seleccionado=false excluye del
/// ICollectionView todas las filas con ese valor; cambiarlo dispara <paramref name="alCambiar"/>
/// (InventarioGeneralViewModel.VistaFiltrada.Refresh) de inmediato.
/// </summary>
public sealed partial class OpcionFiltroColumna : ObservableObject
{
    private readonly Action _alCambiar;

    public string Valor { get; }

    [ObservableProperty]
    private bool _seleccionado;

    public OpcionFiltroColumna(string valor, bool seleccionado, Action alCambiar)
    {
        Valor = valor;
        _seleccionado = seleccionado;
        _alCambiar = alCambiar;
    }

    partial void OnSeleccionadoChanged(bool value) => _alCambiar();
}

/// <summary>
/// ViewModel de la pestaña "Inventario": búsqueda de solo lectura contra HighJump/AAD
/// (AadWmsService.BuscarInventarioAsync) para consultar existencias reales del WMS, sin
/// tocar Neon ni alterar el flujo del Generador Principal o del Histórico. Sobre el lote
/// descargado se aplican además filtros estilo Excel por columna (ICollectionView, en
/// memoria, sin ida y vuelta al WMS) para Ubicación, Lote, Cliente y Estatus.
/// </summary>
public sealed partial class InventarioGeneralViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly ICollectionView _vistaFiltrada;

    public ObservableCollection<InventarioItemModel> Resultados { get; } = new();

    /// <summary>Lo que debe enlazar el DataGrid: Resultados ya pasado por los filtros de columna estilo Excel.</summary>
    public ICollectionView VistaFiltrada => _vistaFiltrada;

    public ObservableCollection<OpcionFiltroColumna> OpcionesFiltroUbicacion { get; } = new();
    public ObservableCollection<OpcionFiltroColumna> OpcionesFiltroLote { get; } = new();
    public ObservableCollection<OpcionFiltroColumna> OpcionesFiltroCliente { get; } = new();
    public ObservableCollection<OpcionFiltroColumna> OpcionesFiltroEstatus { get; } = new();

    /// <summary>
    /// "Sin límite" (Valor=null) deja que AadWmsService decida: 500 de salvaguarda si no hay
    /// ningún filtro de texto, o el lote completo sin tope si ya se filtró por Lpn/Sku/Lote/
    /// Ubicacion — nunca se ignora la salvaguarda por completo.
    /// </summary>
    public IReadOnlyList<OpcionLimiteInventario> OpcionesLimite { get; } =
    [
        new("500", 500),
        new("1,000", 1000),
        new("5,000", 5000),
        new("Sin límite", null),
    ];

    [ObservableProperty]
    private string? _filtroLpn;

    [ObservableProperty]
    private string? _filtroSku;

    [ObservableProperty]
    private string? _filtroLote;

    [ObservableProperty]
    private string? _filtroUbicacion;

    [ObservableProperty]
    private string? _filtroCliente;

    [ObservableProperty]
    private bool _soloDisponibles;

    [ObservableProperty]
    private OpcionLimiteInventario _limiteSeleccionado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuscarCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportarExcelCommand))]
    private bool _estaCargando;

    [ObservableProperty]
    private string _mensajeEstado = "Sin buscar todavía.";

    public InventarioGeneralViewModel(AppServices services)
    {
        _services = services;
        _limiteSeleccionado = OpcionesLimite[0];

        _vistaFiltrada = CollectionViewSource.GetDefaultView(Resultados);
        _vistaFiltrada.Filter = FiltrarFila;

        if (!services.WmsDisponible)
        {
            _mensajeEstado = $"HighJump/AAD no disponible: {services.ErrorConfiguracionWms}";
        }
    }

    private bool PuedeBuscar() => !EstaCargando;

    [RelayCommand(CanExecute = nameof(PuedeBuscar))]
    private async Task BuscarAsync()
    {
        if (_services.Wms is null)
        {
            MensajeEstado = $"HighJump/AAD no disponible: {_services.ErrorConfiguracionWms}";
            return;
        }

        EstaCargando = true;
        MensajeEstado = "Buscando...";
        var cronometro = Stopwatch.StartNew();
        try
        {
            var filtros = new InventarioFiltrosModel(
                FiltroLpn, FiltroSku, FiltroLote, FiltroUbicacion, FiltroCliente, SoloDisponibles)
            {
                LimiteMaximo = LimiteSeleccionado.Valor,
            };

            var resultados = await _services.Wms.BuscarInventarioAsync(filtros);
            cronometro.Stop();

            Resultados.Clear();
            foreach (var item in resultados)
            {
                Resultados.Add(item);
            }

            ReconstruirOpcionesDeColumnas();
            _vistaFiltrada.Refresh();

            var topEfectivo = DeterminarTopEfectivoParaMensaje();
            MensajeEstado = topEfectivo.HasValue && resultados.Count == topEfectivo.Value
                ? $"{resultados.Count} resultado(s) (límite de {topEfectivo} alcanzado — afina los filtros o sube el tope) en {cronometro.ElapsedMilliseconds} ms."
                : $"{resultados.Count} resultado(s) en {cronometro.ElapsedMilliseconds} ms.";
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            // SqlException.Number == -2 es específicamente "Execution Timeout Expired": el
            // WMS no respondió dentro del CommandTimeout de 5s configurado en AadWmsService.
            cronometro.Stop();
            Resultados.Clear();
            MensajeEstado = "El WMS (HighJump/AAD) no respondió dentro de 5 segundos. Intenta de nuevo o avisa a soporte si persiste.";
        }
        catch (Exception ex)
        {
            cronometro.Stop();
            Resultados.Clear();
            MensajeEstado = $"No se pudo consultar el inventario: {ex.Message}";
        }
        finally
        {
            EstaCargando = false;
            ExportarExcelCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Replica en el cliente la misma regla de AadWmsService.BuscarInventarioAsync, solo
    /// para decidir si el mensaje de estado debe advertir que el resultado quedó recortado.
    /// </summary>
    private int? DeterminarTopEfectivoParaMensaje()
    {
        var hayFiltroTexto = !string.IsNullOrWhiteSpace(FiltroLpn)
            || !string.IsNullOrWhiteSpace(FiltroSku)
            || !string.IsNullOrWhiteSpace(FiltroLote)
            || !string.IsNullOrWhiteSpace(FiltroUbicacion);

        return LimiteSeleccionado.Valor is null or 0
            ? (hayFiltroTexto ? null : 500)
            : LimiteSeleccionado.Valor;
    }

    /// <summary>Reconstruye la lista de valores únicos de cada columna filtrable a partir del lote recién descargado, todos preseleccionados (sin filtrar).</summary>
    private void ReconstruirOpcionesDeColumnas()
    {
        ReconstruirOpciones(OpcionesFiltroUbicacion, Resultados.Select(r => r.Ubicacion));
        ReconstruirOpciones(OpcionesFiltroLote, Resultados.Select(r => r.Lote ?? string.Empty));
        ReconstruirOpciones(OpcionesFiltroCliente, Resultados.Select(r => r.Cliente));
        ReconstruirOpciones(OpcionesFiltroEstatus, Resultados.Select(r => r.EstatusTarima));
    }

    private void ReconstruirOpciones(ObservableCollection<OpcionFiltroColumna> destino, IEnumerable<string> valores)
    {
        destino.Clear();
        foreach (var valor in valores.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
        {
            destino.Add(new OpcionFiltroColumna(valor, seleccionado: true, alCambiar: _vistaFiltrada.Refresh));
        }
    }

    private bool FiltrarFila(object obj)
    {
        if (obj is not InventarioItemModel item)
        {
            return false;
        }

        return PasaFiltroColumna(OpcionesFiltroUbicacion, item.Ubicacion)
            && PasaFiltroColumna(OpcionesFiltroLote, item.Lote ?? string.Empty)
            && PasaFiltroColumna(OpcionesFiltroCliente, item.Cliente)
            && PasaFiltroColumna(OpcionesFiltroEstatus, item.EstatusTarima);
    }

    private static bool PasaFiltroColumna(ObservableCollection<OpcionFiltroColumna> opciones, string valor)
    {
        // Antes de reconstruir las opciones (p. ej. justo tras limpiar filtros) la lista
        // puede estar vacía: en ese caso no hay nada que excluir.
        foreach (var opcion in opciones)
        {
            if (string.Equals(opcion.Valor, valor, StringComparison.OrdinalIgnoreCase))
            {
                return opcion.Seleccionado;
            }
        }

        return true;
    }

    private List<InventarioItemModel> ObtenerFilasVisibles() =>
        _vistaFiltrada.Cast<InventarioItemModel>().ToList();

    /// <summary>Botón "Todos" del popup de filtro de una columna: marca todos sus valores (CommandParameter = la colección de esa columna).</summary>
    [RelayCommand]
    private static void MarcarTodosColumna(IEnumerable<OpcionFiltroColumna>? opciones)
    {
        if (opciones is null)
        {
            return;
        }

        foreach (var opcion in opciones)
        {
            opcion.Seleccionado = true;
        }
    }

    /// <summary>Botón "Ninguno" del popup de filtro de una columna: desmarca todos sus valores (CommandParameter = la colección de esa columna).</summary>
    [RelayCommand]
    private static void MarcarNingunoColumna(IEnumerable<OpcionFiltroColumna>? opciones)
    {
        if (opciones is null)
        {
            return;
        }

        foreach (var opcion in opciones)
        {
            opcion.Seleccionado = false;
        }
    }

    /// <summary>Restablece tanto los filtros de búsqueda enviados al WMS como los filtros de columna estilo Excel ya aplicados sobre el lote descargado.</summary>
    [RelayCommand]
    private void LimpiarFiltros()
    {
        FiltroLpn = null;
        FiltroSku = null;
        FiltroLote = null;
        FiltroUbicacion = null;
        FiltroCliente = null;
        SoloDisponibles = false;
        LimiteSeleccionado = OpcionesLimite[0];

        foreach (var opcion in OpcionesFiltroUbicacion
            .Concat(OpcionesFiltroLote)
            .Concat(OpcionesFiltroCliente)
            .Concat(OpcionesFiltroEstatus))
        {
            opcion.Seleccionado = true;
        }

        _vistaFiltrada.Refresh();
        ExportarExcelCommand.NotifyCanExecuteChanged();
        MensajeEstado = "Filtros restablecidos.";
    }

    private bool PuedeExportar() => !EstaCargando && ObtenerFilasVisibles().Count > 0;

    /// <summary>Exporta a Excel las filas visibles tras los filtros de columna estilo Excel (no necesariamente todo el lote descargado).</summary>
    [RelayCommand(CanExecute = nameof(PuedeExportar))]
    private void ExportarExcel()
    {
        var filas = ObtenerFilasVisibles();
        if (filas.Count == 0)
        {
            MensajeEstado = "No hay filas visibles para exportar.";
            return;
        }

        var dialogo = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Archivos Excel (*.xlsx)|*.xlsx",
            FileName = $"Inventario_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
        };

        if (dialogo.ShowDialog() != true)
        {
            return;
        }

        try
        {
            using var libro = new XLWorkbook();
            var hoja = libro.Worksheets.Add("Inventario");

            string[] encabezados =
                ["LPN", "SKU", "Descripción", "Lote", "Ubicación", "Cliente", "Total", "Retenida", "Disponible", "Caducidad", "Estatus"];
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
                var item = filas[i];
                var fila = i + 2;

                hoja.Cell(fila, 1).Value = item.Lpn;
                hoja.Cell(fila, 2).Value = item.Sku;
                hoja.Cell(fila, 3).Value = item.Descripcion ?? string.Empty;
                hoja.Cell(fila, 4).Value = item.Lote ?? string.Empty;
                hoja.Cell(fila, 5).Value = item.Ubicacion;
                hoja.Cell(fila, 6).Value = item.Cliente;
                hoja.Cell(fila, 7).Value = item.CantidadTotal;
                hoja.Cell(fila, 8).Value = item.CantidadRetenida;
                hoja.Cell(fila, 9).Value = item.CantidadDisponible;

                if (item.Caducidad.HasValue)
                {
                    var celdaFecha = hoja.Cell(fila, 10);
                    celdaFecha.Value = item.Caducidad.Value;
                    celdaFecha.Style.DateFormat.Format = "yyyy-mm-dd";
                }

                hoja.Cell(fila, 11).Value = item.EstatusTarima;
            }

            hoja.SheetView.FreezeRows(1);
            hoja.Columns().AdjustToContents();

            libro.SaveAs(dialogo.FileName);
            MensajeEstado = $"Se exportaron {filas.Count} registro(s) a {Path.GetFileName(dialogo.FileName)}.";
        }
        catch (Exception ex)
        {
            MensajeEstado = $"No se pudo exportar el archivo: {ex.Message}";
        }
    }
}
