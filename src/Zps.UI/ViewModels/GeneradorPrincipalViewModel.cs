using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zps.Core;
using Zps.Core.Models;
using Zps.Data.Models;
using Zps.UI.Models;
using Zps.UI.Services;
using Zps.UI.Views;

namespace Zps.UI.ViewModels;

/// <summary>
/// ViewModel de la pestaña "Generador Principal": carga de Excel, selección de cliente/
/// impresora, validación en vivo (MinisoLpnEngine) y orquestación de generación + impresión
/// (ConsecutivosService -> HistoricoService -> PrinterService), con vista previa vía
/// LabelaryPreviewService.
///
/// Solo procesa las filas marcadas con el checkbox "_Seleccionado" (columna oculta de
/// control, con selector "Todo" en la cabecera), y reproduce el indexado especial de
/// bultos del original: para el cliente MYC, {INDICE}/{TOTAL} se recalculan relativos al
/// lote que se está imprimiendo (1..N filas seleccionadas); para el resto, reflejan la
/// posición absoluta de la fila y el tamaño de la hoja completa (mismo criterio que
/// run_print/confirmar_print en app_centralizada.py).
///
/// El selector de Hoja de Excel es el único maestro de contexto: no existe un selector de
/// Cliente independiente. Al cambiar de hoja, el código de la hoja (p. ej. "MNS", "MYC",
/// "PRM") se mapea automáticamente contra cat_clientes por coincidencia exacta de código o
/// nombre; si no hay coincidencia, no se asume ningún cliente por defecto — se avisa y se
/// habilita un selector de excepción para elegirlo manualmente.
/// </summary>
public sealed partial class GeneradorPrincipalViewModel : ObservableObject
{
    /// <summary>
    /// Impresora "mock" para pruebas/auditoría visual: en vez de mandar bytes RAW por
    /// winspool, abre una ventana con el renderizado de Labelary de cada etiqueta generada.
    /// El resto del flujo (reserva de folios, validaciones, guardado en histórico) corre
    /// idéntico a una impresión real.
    /// </summary>
    public const string ImpresoraVirtualNombre = "[VIRTUAL] Previsualizador / Testing";

    private readonly AppServices _services;

    public ObservableCollection<ClienteCatalogoRecord> Clientes { get; } = new();

    public ObservableCollection<string> Impresoras { get; } = new();

    public ObservableCollection<string> Hojas { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerarEImprimirCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerarSinImprimirCommand))]
    [NotifyPropertyChangedFor(nameof(ClienteActivoTexto))]
    private ClienteCatalogoRecord? _clienteSeleccionado;

    /// <summary>
    /// True cuando el nombre de la hoja seleccionada no coincidió con ningún cliente de
    /// cat_clientes: habilita el selector de excepción para elegirlo a mano.
    /// </summary>
    [ObservableProperty]
    private bool _requiereSeleccionManualDeCliente;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerarEImprimirCommand))]
    private string? _impresoraSeleccionada;

    [ObservableProperty]
    private string? _hojaSeleccionada;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerarEImprimirCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerarSinImprimirCommand))]
    private DataTable? _datosExcel;

    [ObservableProperty]
    private DataView? _vistaDatos;

    [ObservableProperty]
    private DataRowView? _filaSeleccionada;

    [ObservableProperty]
    private string? _rutaArchivo;

    [ObservableProperty]
    private string _solicitante = string.Empty;

    [ObservableProperty]
    private string _arribo = string.Empty;

    [ObservableProperty]
    private string _estadoMensaje = "Listo. Carga un Excel para comenzar.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerarEImprimirCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerarSinImprimirCommand))]
    private bool _estaOcupado;

    [ObservableProperty]
    private double _progreso;

    [ObservableProperty]
    private byte[]? _imagenPreview;

    public string NombreArchivo => string.IsNullOrEmpty(RutaArchivo) ? "Sin archivo" : Path.GetFileName(RutaArchivo);

    public string ClienteActivoTexto => ClienteSeleccionado is null
        ? "Sin cliente identificado"
        : $"{ClienteSeleccionado.Nombre} ({ClienteSeleccionado.EstrategiaLpn})";

    public GeneradorPrincipalViewModel(AppServices services)
    {
        _services = services;
    }

    public async Task CargarDatosInicialesAsync()
    {
        EstaOcupado = true;
        try
        {
            Impresoras.Clear();
            Impresoras.Add(ImpresoraVirtualNombre);
            foreach (var impresora in _services.Impresoras.ListarImpresorasInstaladas())
            {
                Impresoras.Add(impresora);
            }

            ImpresoraSeleccionada = Impresoras.FirstOrDefault(i =>
                    i.Contains("Zebra", StringComparison.OrdinalIgnoreCase) ||
                    i.Contains("ZDesigner", StringComparison.OrdinalIgnoreCase))
                ?? Impresoras.FirstOrDefault();

            IReadOnlyList<ClienteCatalogoRecord> clientes;
            if (_services.CatalogoClientes is not null)
            {
                try
                {
                    clientes = await _services.CatalogoClientes.ObtenerActivosAsync();
                    await _services.CacheLocal.ReplicarClientesAsync(clientes);
                }
                catch (Exception ex)
                {
                    EstadoMensaje = $"No se pudo consultar el catálogo en Neon ({ex.Message}); usando caché local.";
                    clientes = (await _services.CacheLocal.ObtenerClientesAsync()).Where(c => c.Activo).ToList();
                }
            }
            else
            {
                clientes = (await _services.CacheLocal.ObtenerClientesAsync()).Where(c => c.Activo).ToList();
            }

            Clientes.Clear();
            foreach (var cliente in clientes.OrderBy(c => c.Codigo))
            {
                Clientes.Add(cliente);
            }
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    [RelayCommand]
    private async Task CargarExcelAsync()
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog { Filter = "Archivos Excel (*.xlsx)|*.xlsx" };
        if (dialogo.ShowDialog() != true)
        {
            return;
        }

        EstaOcupado = true;
        try
        {
            RutaArchivo = dialogo.FileName;
            OnPropertyChanged(nameof(NombreArchivo));

            var nombresHojas = await Task.Run(() => LeerNombresHojas(RutaArchivo));
            Hojas.Clear();
            foreach (var hoja in nombresHojas)
            {
                Hojas.Add(hoja);
            }

            HojaSeleccionada = Hojas.FirstOrDefault();
        }
        catch (Exception ex)
        {
            EstadoMensaje = $"No se pudo leer el archivo: {ex.Message}";
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    partial void OnDatosExcelChanged(DataTable? value)
    {
        // Reacciona a los toggles del checkbox "_Seleccionado" (edición de celda del DataGrid,
        // no pasa por [ObservableProperty]) para habilitar/deshabilitar el botón de generar.
        if (value is not null)
        {
            value.ColumnChanged += DatosExcel_OnColumnChanged;
        }
    }

    private void DatosExcel_OnColumnChanged(object sender, DataColumnChangeEventArgs e)
    {
        if (e.Column?.ColumnName == "_Seleccionado")
        {
            GenerarEImprimirCommand.NotifyCanExecuteChanged();
            GenerarSinImprimirCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void SeleccionarTodos(bool marcar)
    {
        if (DatosExcel is null)
        {
            return;
        }

        foreach (DataRow fila in DatosExcel.Rows)
        {
            fila["_Seleccionado"] = marcar;
        }
    }

    partial void OnHojaSeleccionadaChanged(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        // La hoja es el selector maestro: cada cambio de hoja resuelve el cliente activo
        // contra cat_clientes ANTES de cargar los datos, para que la validación y el
        // indexado de bultos ya usen la estrategia correcta desde la primera fila.
        MapearClientePorNombreDeHoja(value);

        if (!string.IsNullOrEmpty(RutaArchivo))
        {
            _ = CargarHojaAsync(RutaArchivo, value);
        }
    }

    /// <summary>
    /// Alias operativos: el piso nombra hojas/arribos con convenciones que no siempre
    /// coinciden con el código registrado en cat_clientes (p. ej. "MNS" para el régimen
    /// MINISO). Sin este alias, esas hojas caerían siempre al selector de excepción.
    /// </summary>
    private static readonly Dictionary<string, string> AliasClientesOperativos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MNS"] = "MINISO",
    };

    /// <summary>
    /// Resuelve el cliente activo a partir del nombre de la hoja (coincidencia exacta,
    /// insensible a mayúsculas, contra el código o el nombre del catálogo, pasando primero
    /// por los alias operativos conocidos). Si no hay coincidencia, no se asume ningún
    /// cliente por defecto: se avisa y se habilita el selector de excepción para que el
    /// operador lo elija a mano.
    /// </summary>
    private void MapearClientePorNombreDeHoja(string nombreHoja)
    {
        var nombreBuscado = AliasClientesOperativos.TryGetValue(nombreHoja.Trim(), out var alias) ? alias : nombreHoja;

        var encontrado = Clientes.FirstOrDefault(c =>
            string.Equals(c.Codigo, nombreBuscado, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Nombre, nombreBuscado, StringComparison.OrdinalIgnoreCase));

        if (encontrado is not null)
        {
            ClienteSeleccionado = encontrado;
            RequiereSeleccionManualDeCliente = false;
            EstadoMensaje = $"Cliente '{encontrado.Codigo}' identificado automáticamente para la hoja '{nombreHoja}'.";
            return;
        }

        ClienteSeleccionado = null;
        RequiereSeleccionManualDeCliente = true;
        EstadoMensaje = $"La hoja '{nombreHoja}' no coincide con ningún cliente de cat_clientes. Selecciónalo manualmente abajo.";

        MessageBox.Show(
            $"La hoja '{nombreHoja}' no coincide (por código ni por nombre) con ningún cliente registrado en el catálogo (cat_clientes).\n\n" +
            "Selecciona el cliente manualmente en el selector de excepción para poder continuar.",
            "Cliente no identificado",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    partial void OnFilaSeleccionadaChanged(DataRowView? value)
    {
        if (value is not null)
        {
            _ = ActualizarPreviewAsync(value.Row);
        }
    }

    partial void OnClienteSeleccionadoChanged(ClienteCatalogoRecord? value)
    {
        if (value is not null)
        {
            RequiereSeleccionManualDeCliente = false;
        }

        RevalidarFilas();
    }

    private async Task CargarHojaAsync(string ruta, string hoja)
    {
        EstaOcupado = true;
        try
        {
            var tabla = await Task.Run(() => LeerHojaComoDataTable(ruta, hoja));
            DatosExcel = tabla;
            VistaDatos = tabla.DefaultView;
            RevalidarFilas();
            EstadoMensaje = $"{tabla.Rows.Count} fila(s) cargadas de la hoja '{hoja}'.";

            // Refresca la vista previa con la plantilla del cliente ya resuelto para esta hoja.
            if (tabla.Rows.Count > 0)
            {
                await ActualizarPreviewAsync(tabla.Rows[0]);
            }
            else
            {
                ImagenPreview = null;
            }
        }
        catch (Exception ex)
        {
            EstadoMensaje = $"Error leyendo la hoja '{hoja}': {ex.Message}";
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    private void RevalidarFilas()
    {
        if (DatosExcel is null)
        {
            return;
        }

        var esMiniso = string.Equals(ClienteSeleccionado?.EstrategiaLpn, "MINISO", StringComparison.OrdinalIgnoreCase);

        foreach (DataRow fila in DatosExcel.Rows)
        {
            if (!esMiniso)
            {
                fila["_EsValido"] = true;
                fila["_MensajeError"] = string.Empty;
                continue;
            }

            var categoria = ObtenerValorColumna(fila, "CATEGORIA");
            var item = ObtenerValorColumna(fila, "ITEM");
            if (string.IsNullOrEmpty(item))
            {
                item = ObtenerValorColumna(fila, "MOTIVO");
            }

            var resultado = MinisoLpnEngine.ValidarCategoriaMotivo(categoria, item);
            fila["_EsValido"] = resultado.EsValido;
            fila["_MensajeError"] = resultado.EsValido ? string.Empty : resultado.MensajeError ?? string.Empty;
        }
    }

    private bool PuedeGenerar() =>
        ClienteSeleccionado is not null &&
        !string.IsNullOrWhiteSpace(ImpresoraSeleccionada) &&
        HayFilasSeleccionadas() &&
        !EstaOcupado;

    /// <summary>
    /// "Generar sin Imprimir" no requiere ninguna impresora seleccionada: pensado para una
    /// estación remota sin Zebra conectada, que solo necesita reservar folios, validar y
    /// dejar los registros listos en historico_impresiones para que otra estación (la que
    /// sí tiene la impresora física) los busque y reimprima.
    /// </summary>
    private bool PuedeGenerarSinImprimir() =>
        ClienteSeleccionado is not null &&
        HayFilasSeleccionadas() &&
        !EstaOcupado;

    private bool HayFilasSeleccionadas() =>
        DatosExcel is not null &&
        DatosExcel.Rows.Cast<DataRow>().Any(fila => fila["_Seleccionado"] is bool marcada && marcada);

    [RelayCommand(CanExecute = nameof(PuedeGenerar))]
    private Task GenerarEImprimirAsync() => EjecutarGeneracionAsync(despacharAImpresora: true);

    [RelayCommand(CanExecute = nameof(PuedeGenerarSinImprimir))]
    private Task GenerarSinImprimirAsync() => EjecutarGeneracionAsync(despacharAImpresora: false);

    private async Task EjecutarGeneracionAsync(bool despacharAImpresora)
    {
        if (DatosExcel is null || ClienteSeleccionado is null)
        {
            return;
        }

        if (despacharAImpresora && string.IsNullOrWhiteSpace(ImpresoraSeleccionada))
        {
            EstadoMensaje = "Selecciona una impresora, o usa 'Generar sin Imprimir' si esta estación no tiene una Zebra conectada.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Solicitante) || string.IsNullOrWhiteSpace(Arribo))
        {
            EstadoMensaje = "Ingresa Solicitante y Arribo antes de generar.";
            return;
        }

        if (_services.Consecutivos is null || _services.Historico is null)
        {
            EstadoMensaje = "Neon no está disponible: no se pueden reservar folios sin conexión.";
            return;
        }

        EstaOcupado = true;
        Progreso = 0;
        try
        {
            RevalidarFilas();
            var esMiniso = string.Equals(ClienteSeleccionado.EstrategiaLpn, "MINISO", StringComparison.OrdinalIgnoreCase);
            var esMyc = string.Equals(ClienteSeleccionado.Codigo, "MYC", StringComparison.OrdinalIgnoreCase);

            // Todas las filas de la hoja (para el indexado absoluto de clientes no-MYC) vs.
            // solo las marcadas con el checkbox (lo único que realmente se genera/imprime).
            var todasLasFilas = DatosExcel.Rows.Cast<DataRow>().ToList();
            var indiceAbsolutoPorFila = todasLasFilas
                .Select((fila, indice) => (fila, indice))
                .ToDictionary(x => x.fila, x => x.indice + 1);

            var filas = todasLasFilas.Where(f => f["_Seleccionado"] is bool marcada && marcada).ToList();
            if (filas.Count == 0)
            {
                EstadoMensaje = "Selecciona al menos una fila (checkbox) para generar e imprimir.";
                return;
            }

            if (esMiniso)
            {
                var invalidas = filas.Where(f => f["_EsValido"] is bool b && !b).ToList();
                if (invalidas.Count > 0)
                {
                    var mensajes = invalidas.Take(10).Select(f => $"Fila {DatosExcel.Rows.IndexOf(f) + 2}: {f["_MensajeError"]}");
                    EstadoMensaje = "Errores de validación, no se generó nada:\n" + string.Join("\n", mensajes);
                    return;
                }
            }

            var anio = DateTime.Now.Year;
            var infoPorFila = new List<(DataRow Fila, TipoSecuenciaLpn Tipo, int? Motivo)>();

            foreach (var fila in filas)
            {
                if (esMiniso)
                {
                    var categoria = ObtenerValorColumna(fila, "CATEGORIA");
                    var item = ObtenerValorColumna(fila, "ITEM");
                    if (string.IsNullOrEmpty(item))
                    {
                        item = ObtenerValorColumna(fila, "MOTIVO");
                    }

                    var validacion = MinisoLpnEngine.ValidarCategoriaMotivo(categoria, item);
                    infoPorFila.Add((fila, validacion.TipoSecuencia!.Value, validacion.Motivo));
                }
                else
                {
                    var lpnExistente = ObtenerValorColumna(fila, "LPN");
                    infoPorFila.Add((fila, MinisoLpnEngine.DeterminarTipoEstandar(lpnExistente), null));
                }
            }

            var folioPorFila = new Dictionary<DataRow, string>();
            var consecutivoPorFila = new Dictionary<DataRow, int>();

            foreach (var grupo in infoPorFila.GroupBy(x => x.Tipo))
            {
                var filasDelGrupo = grupo.ToList();
                var reservados = await _services.Consecutivos.ReservarLoteAsync(grupo.Key, anio, filasDelGrupo.Count);

                for (var i = 0; i < filasDelGrupo.Count; i++)
                {
                    var (fila, tipo, motivo) = filasDelGrupo[i];
                    var consecutivo = reservados[i];
                    var folio = MinisoLpnEngine.FormatearFolio(tipo, consecutivo, anio, motivo);

                    if (!fila.Table.Columns.Contains("LPN"))
                    {
                        fila.Table.Columns.Add("LPN");
                    }

                    fila["LPN"] = folio;
                    folioPorFila[fila] = folio;
                    consecutivoPorFila[fila] = consecutivo;
                }
            }

            var registros = infoPorFila.Select(x => new HistoricoImpresionRecord(
                Lpn: folioPorFila[x.Fila],
                Consecutivo: consecutivoPorFila[x.Fila],
                Tipo: x.Tipo.ToString(),
                Cliente: ClienteSeleccionado.Codigo,
                Solicitante: Solicitante.Trim(),
                Arribo: Arribo.Trim(),
                // UTC puro (Offset = 0): Npgsql rechaza DateTimeOffset con offset local
                // distinto de cero al escribir en columnas timestamptz (fecha_hora en Neon).
                FechaHora: DateTimeOffset.UtcNow,
                Sku: NuloSiVacio(ObtenerValorColumna(x.Fila, "SKU")),
                Lote: NuloSiVacio(ObtenerValorColumna(x.Fila, "LOTE")),
                Cantidad: ExtraerCantidad(x.Fila),
                VariablesJson: JsonSerializer.Serialize(FilaADiccionario(x.Fila))))
                .ToList();

            await _services.Historico.InsertarLoteAsync(registros);
            await _services.CacheLocal.ReplicarHistoricoAsync(registros);

            var total = filas.Count;
            var totalHojaCompleta = todasLasFilas.Count;
            var errores = 0;
            var esImpresoraVirtual = despacharAImpresora && string.Equals(ImpresoraSeleccionada, ImpresoraVirtualNombre, StringComparison.Ordinal);
            var auditoriaVirtual = esImpresoraVirtual ? new List<AuditoriaEtiqueta>(total) : null;

            for (var i = 0; i < infoPorFila.Count; i++)
            {
                var fila = infoPorFila[i].Fila;

                // "Generar sin Imprimir": el folio ya quedó reservado y guardado en
                // historico_impresiones arriba; no hace falta generar ZPL ni tocar ninguna
                // impresora (real o virtual) — otra estación con la Zebra conectada lo
                // reimprimirá después desde la pestaña Histórico.
                if (despacharAImpresora)
                {
                    // Indexado especial de bultos: MYC renumera relativo al lote que se
                    // imprime ahora mismo (1..N seleccionadas); el resto de clientes usa la
                    // posición absoluta de la fila y el tamaño de la hoja completa (aunque
                    // se imprima solo un subconjunto), igual que run_print/confirmar_print
                    // en app_centralizada.py.
                    int indiceParaZpl;
                    int totalParaZpl;
                    if (esMyc)
                    {
                        indiceParaZpl = i + 1;
                        totalParaZpl = total;
                    }
                    else
                    {
                        indiceParaZpl = indiceAbsolutoPorFila[fila];
                        totalParaZpl = totalHojaCompleta;
                    }

                    var datos = FilaADiccionario(fila);
                    var zpl = ZplTemplateEngine.Generar(
                        ClienteSeleccionado.PlantillaZpl ?? string.Empty,
                        ClienteSeleccionado.MapeoColumnas,
                        datos,
                        indiceParaZpl,
                        totalParaZpl);

                    if (esImpresoraVirtual)
                    {
                        // Modo auditoría: nunca toca el spooler real; renderiza vía Labelary
                        // para revisión visual, con la reserva de folios y el guardado en
                        // histórico ya hechos exactamente igual que en una impresión real.
                        var resultadoPreview = await _services.Preview.GenerarVistaPreviaAsync(zpl);
                        auditoriaVirtual!.Add(new AuditoriaEtiqueta(
                            folioPorFila[fila],
                            resultadoPreview.Exito ? resultadoPreview.ImagenPng : null,
                            resultadoPreview.Exito ? null : resultadoPreview.Error));
                    }
                    else
                    {
                        var resultado = await _services.Impresoras.EncolarAsync(ImpresoraSeleccionada!, zpl, $"LPN {folioPorFila[fila]}");
                        if (!resultado.Exito)
                        {
                            errores++;
                        }
                    }
                }

                Progreso = (i + 1) / (double)total;
            }

            EstadoMensaje = !despacharAImpresora
                ? $"Proceso terminado: {total} etiqueta(s) generadas y guardadas en histórico (sin imprimir). Ya están disponibles para buscarlas y reimprimirlas desde la pestaña Histórico."
                : esImpresoraVirtual
                    ? $"Proceso terminado (modo virtual): {total} etiqueta(s) generadas y guardadas en histórico, ninguna enviada a una impresora real."
                    : $"Proceso terminado: {total} etiqueta(s), {errores} error(es).";
            VistaDatos = DatosExcel.DefaultView;

            if (filas.Count > 0)
            {
                await ActualizarPreviewAsync(filas[0]);
            }

            if (auditoriaVirtual is not null)
            {
                // Se abre en el hilo de UI explícitamente: GenerarVistaPreviaAsync usa
                // ConfigureAwait(false) internamente, así que la continuación pudo quedar
                // en un hilo de threadpool en vez del hilo de despacho de WPF.
                Application.Current.Dispatcher.Invoke(() => new AuditoriaVirtualWindow(auditoriaVirtual)
                {
                    Owner = Application.Current.MainWindow
                }.Show());
            }
        }
        catch (Exception ex)
        {
            EstadoMensaje = $"Error durante la generación: {ex.Message}";
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    private async Task ActualizarPreviewAsync(DataRow fila)
    {
        if (ClienteSeleccionado?.PlantillaZpl is null)
        {
            ImagenPreview = null;
            return;
        }

        var datos = FilaADiccionario(fila);
        var zpl = ZplTemplateEngine.Generar(ClienteSeleccionado.PlantillaZpl, ClienteSeleccionado.MapeoColumnas, datos, 1, 1);
        var resultado = await _services.Preview.GenerarVistaPreviaAsync(zpl);

        ImagenPreview = resultado.Exito ? resultado.ImagenPng : null;
        if (!resultado.Exito)
        {
            EstadoMensaje = $"Vista previa no disponible: {resultado.Error}";
        }
    }

    private static Dictionary<string, string?> FilaADiccionario(DataRow fila)
    {
        var dict = new Dictionary<string, string?>();
        foreach (DataColumn columna in fila.Table.Columns)
        {
            if (columna.ColumnName.StartsWith('_'))
            {
                continue;
            }

            dict[columna.ColumnName] = fila[columna]?.ToString();
        }

        return dict;
    }

    private static string ObtenerValorColumna(DataRow fila, string nombreColumnaBuscada)
    {
        foreach (DataColumn columna in fila.Table.Columns)
        {
            if (string.Equals(columna.ColumnName, nombreColumnaBuscada, StringComparison.OrdinalIgnoreCase))
            {
                return fila[columna]?.ToString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string? NuloSiVacio(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    /// <summary>
    /// La columna de cantidad se llama distinto según el cliente (CANTIDAD en la mayoría
    /// de las plantillas reales, QTY en MYC/AXO): se prueba primero CANTIDAD y, si no hay
    /// valor, se cae a QTY, igual que el patrón ITEM/MOTIVO ya usado para MINISO.
    /// </summary>
    private static decimal? ExtraerCantidad(DataRow fila)
    {
        var texto = ObtenerValorColumna(fila, "CANTIDAD");
        if (string.IsNullOrWhiteSpace(texto))
        {
            texto = ObtenerValorColumna(fila, "QTY");
        }

        return decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var cantidad)
            ? cantidad
            : null;
    }

    private static List<string> LeerNombresHojas(string ruta)
    {
        using var libro = new XLWorkbook(ruta);
        return libro.Worksheets.Select(w => w.Name).ToList();
    }

    private static DataTable LeerHojaComoDataTable(string ruta, string hoja)
    {
        using var libro = new XLWorkbook(ruta);
        var hojaXl = libro.Worksheet(hoja);
        var tabla = new DataTable();

        var rango = hojaXl.RangeUsed();
        if (rango is null)
        {
            return tabla;
        }

        var filas = rango.RowsUsed().ToList();
        if (filas.Count == 0)
        {
            return tabla;
        }

        var anchoColumnas = rango.ColumnCount();
        var encabezado = filas[0];

        var nombresColumnas = new List<string>();
        for (var c = 1; c <= anchoColumnas; c++)
        {
            var texto = encabezado.Cell(c).GetString().Trim();
            nombresColumnas.Add(string.IsNullOrEmpty(texto) ? $"COL{c}" : texto.ToUpperInvariant());
        }

        foreach (var nombre in HacerUnicos(nombresColumnas))
        {
            tabla.Columns.Add(nombre, typeof(string));
        }

        tabla.Columns.Add("_EsValido", typeof(bool));
        tabla.Columns.Add("_MensajeError", typeof(string));
        tabla.Columns.Add("_Seleccionado", typeof(bool));

        foreach (var filaXl in filas.Skip(1))
        {
            var nuevaFila = tabla.NewRow();
            for (var c = 1; c <= anchoColumnas; c++)
            {
                nuevaFila[c - 1] = filaXl.Cell(c).GetString();
            }

            nuevaFila["_EsValido"] = true;
            nuevaFila["_MensajeError"] = string.Empty;
            nuevaFila["_Seleccionado"] = true; // por defecto todas las filas arrancan marcadas, como en el original
            tabla.Rows.Add(nuevaFila);
        }

        return tabla;
    }

    private static List<string> HacerUnicos(List<string> nombres)
    {
        var vistos = new Dictionary<string, int>();
        var resultado = new List<string>();

        foreach (var nombre in nombres)
        {
            if (!vistos.TryGetValue(nombre, out var contador))
            {
                vistos[nombre] = 1;
                resultado.Add(nombre);
            }
            else
            {
                vistos[nombre] = contador + 1;
                resultado.Add($"{nombre}_{contador + 1}");
            }
        }

        return resultado;
    }
}
