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
    [NotifyCanExecuteChangedFor(nameof(EditarYReimprimirCommand))]
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
        EditarYReimprimirCommand.NotifyCanExecuteChanged();
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
            || Coincide(registro.Lote, texto)
            || Coincide(registro.Cajas, texto);
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

    /// <summary>
    /// Se dispara cuando "Reimprimir Selección" ya cargó los registros en el Generador
    /// Principal: MainViewModel se suscribe para cambiar la pestaña activa hacia él.
    /// </summary>
    public event Action? SolicitoCambiarAGeneradorPrincipal;

    private bool PuedeReimprimir() => _seleccionados.Count > 0 && !EstaOcupado;

    /// <summary>
    /// Ya no imprime directamente: carga los registros seleccionados (con su LPN,
    /// TarimaActual/TarimaTotal originales) en el grid del Generador Principal y cambia la
    /// pestaña activa hacia allá, para que el operador los imprima tal cual con "Imprimir",
    /// o ajuste la selección / "Impresión por lotes" para partir el lote — lo que dispara la
    /// regla crítica de recálculo y UPDATE de tarima en esa pestaña.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeReimprimir))]
    private async Task ReimprimirSeleccionAsync()
    {
        if (_seleccionados.Count == 0)
        {
            return;
        }

        EstaOcupado = true;
        try
        {
            var seleccionados = _seleccionados;
            await _generador.CargarRegistrosDesdeHistoricoAsync(seleccionados);
            EstadoMensaje = $"{seleccionados.Count} registro(s) cargados en el Generador Principal para reimprimir.";
            SolicitoCambiarAGeneradorPrincipal?.Invoke();
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    private bool PuedeEditar() => _seleccionados.Count == 1 && !EstaOcupado;

    /// <summary>
    /// Edita Sku/Lote/Cantidad de un único folio ya impreso y lo reimprime, conservando su
    /// LPN y su TarimaActual/TarimaTotal originales (el "X de N" no cambia solo porque se
    /// corrigió un dato). Cada campo modificado queda registrado en historico_cambios_log,
    /// en Neon si está disponible y siempre en la caché local.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeEditar))]
    private async Task EditarYReimprimirAsync()
    {
        if (_seleccionados.Count != 1)
        {
            return;
        }

        var impresora = _generador.ImpresoraSeleccionada;
        if (string.IsNullOrWhiteSpace(impresora))
        {
            EstadoMensaje = "Selecciona una impresora en la pestaña Generador Principal antes de reimprimir.";
            return;
        }

        var registro = _seleccionados[0];
        var dialogo = new EditarReimpresionWindow(registro) { Owner = Application.Current.MainWindow };
        if (dialogo.ShowDialog() != true)
        {
            return;
        }

        EstaOcupado = true;
        try
        {
            var cambios = new List<CambioCampo>();
            if (!string.Equals(registro.Sku, dialogo.NuevoSku, StringComparison.Ordinal))
            {
                cambios.Add(new CambioCampo("sku", registro.Sku, dialogo.NuevoSku));
            }

            if (!string.Equals(registro.Lote, dialogo.NuevoLote, StringComparison.Ordinal))
            {
                cambios.Add(new CambioCampo("lote", registro.Lote, dialogo.NuevoLote));
            }

            if (registro.Cantidad != dialogo.NuevaCantidad)
            {
                cambios.Add(new CambioCampo(
                    "cantidad",
                    registro.Cantidad?.ToString(CultureInfo.InvariantCulture),
                    dialogo.NuevaCantidad?.ToString(CultureInfo.InvariantCulture)));
            }

            if (!string.Equals(registro.Cajas, dialogo.NuevoCajas, StringComparison.Ordinal))
            {
                cambios.Add(new CambioCampo("cajas", registro.Cajas, dialogo.NuevoCajas));
            }

            var actualizado = registro with
            {
                Sku = dialogo.NuevoSku,
                Lote = dialogo.NuevoLote,
                Cantidad = dialogo.NuevaCantidad,
                Cajas = dialogo.NuevoCajas
            };

            if (cambios.Count > 0)
            {
                var usuario = Environment.UserName;

                if (_services.Historico is not null)
                {
                    try
                    {
                        await _services.Historico.ActualizarConAuditoriaAsync(actualizado, cambios, usuario);
                    }
                    catch (Exception ex)
                    {
                        EstadoMensaje = $"No se pudo actualizar Neon ({ex.Message}); se guardó solo en caché local.";
                    }
                }

                await _services.CacheLocal.ActualizarConAuditoriaAsync(actualizado, cambios, usuario);

                for (var idx = 0; idx < Registros.Count; idx++)
                {
                    if (Registros[idx].Lpn == actualizado.Lpn)
                    {
                        Registros[idx] = actualizado;
                        break;
                    }
                }
            }

            var cliente = _generador.Clientes.FirstOrDefault(c =>
                string.Equals(c.Codigo, actualizado.Cliente, StringComparison.OrdinalIgnoreCase));

            if (cliente?.PlantillaZpl is null)
            {
                EstadoMensaje = "Los cambios se guardaron, pero no se pudo reimprimir: no se encontró la plantilla ZPL del cliente.";
                return;
            }

            var datos = string.IsNullOrEmpty(actualizado.VariablesJson)
                ? new Dictionary<string, string?>()
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(actualizado.VariablesJson) ?? new Dictionary<string, string?>();

            // A diferencia de la reimpresión en lote (que solo rellena si el dato falta en
            // variables_json), aquí los campos recién editados siempre pisan la fila
            // original: el usuario acaba de corregirlos a propósito.
            datos["LPN"] = actualizado.Lpn;
            datos["SKU"] = actualizado.Sku;
            datos["LOTE"] = actualizado.Lote;
            if (actualizado.Cantidad.HasValue)
            {
                var cantidadTexto = actualizado.Cantidad.Value.ToString(CultureInfo.InvariantCulture);
                datos["CANTIDAD"] = cantidadTexto;
                datos["QTY"] = cantidadTexto;
            }

            datos["CAJAS"] = actualizado.Cajas;

            var indiceParaZpl = actualizado.TarimaActual ?? 1;
            var totalParaZpl = actualizado.TarimaTotal ?? 1;
            var zpl = ZplTemplateEngine.Generar(cliente.PlantillaZpl, cliente.MapeoColumnas, datos, indiceParaZpl, totalParaZpl);
            var resultado = await _services.Impresoras.EncolarAsync(impresora, zpl, $"Reimpresión editada {actualizado.Lpn}");

            EstadoMensaje = resultado.Exito
                ? $"'{actualizado.Lpn}' actualizado y reimpreso correctamente ({cambios.Count} campo(s) modificado(s))."
                : $"'{actualizado.Lpn}' actualizado, pero la reimpresión falló: {resultado.Error}";
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

            string[] encabezados = { "LPN", "Consecutivo", "Tipo", "Cliente", "Solicitante", "Arribo", "SKU", "Lote", "Cantidad", "Cajas", "Fecha/Hora" };
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

                hoja.Cell(fila, 10).Value = registro.Cajas ?? string.Empty;

                var celdaFecha = hoja.Cell(fila, 11);
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
            EditarYReimprimirCommand.NotifyCanExecuteChanged();
            ExportarExcelCommand.NotifyCanExecuteChanged();
        }
    }
}
