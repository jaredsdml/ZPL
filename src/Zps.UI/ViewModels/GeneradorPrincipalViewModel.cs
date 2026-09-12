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

    /// <summary>
    /// Plantilla administrada en vivo (plantillas_zpl) resuelta para la hoja actualmente
    /// seleccionada, si existe y está activa. Cuando es null, se usa el fallback existente
    /// (ClienteSeleccionado.PlantillaZpl + MapeoColumnas), para no romper compatibilidad con
    /// clientes que aún no migraron a este mecanismo.
    /// </summary>
    private PlantillaZplRecord? _plantillaDinamica;

    /// <summary>
    /// Folios recién reservados por "Generar" (LPN + tipo/consecutivo por fila) que todavía
    /// no se insertaron en historico_impresiones: "Guardar" los consume y los limpia. Se
    /// mantiene aparte del DataTable porque el tipo/consecutivo de ConsecutivosService no
    /// tiene sentido como columna visible de la grilla.
    /// </summary>
    private List<(DataRow Fila, TipoSecuenciaLpn Tipo, int Consecutivo)>? _reservaPendiente;

    public ObservableCollection<ClienteCatalogoRecord> Clientes { get; } = new();

    public ObservableCollection<string> Impresoras { get; } = new();

    public ObservableCollection<string> Hojas { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerarOGuardarCommand))]
    [NotifyPropertyChangedFor(nameof(ClienteActivoTexto))]
    private ClienteCatalogoRecord? _clienteSeleccionado;

    /// <summary>
    /// True cuando el nombre de la hoja seleccionada no coincidió con ningún cliente de
    /// cat_clientes: habilita el selector de excepción para elegirlo a mano.
    /// </summary>
    [ObservableProperty]
    private bool _requiereSeleccionManualDeCliente;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImprimirCommand))]
    private string? _impresoraSeleccionada;

    [ObservableProperty]
    private string? _hojaSeleccionada;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerarOGuardarCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImprimirCommand))]
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

    /// <summary>
    /// Consecutivo dinámico de tarimas: en false (por defecto), {INDICE}/{TOTAL} reflejan la
    /// posición absoluta de la fila y el tamaño de la hoja completa cargada; en true, se
    /// renumeran relativos únicamente a las filas marcadas con el checkbox de selección
    /// (1..Y), igual que ya hacía MYC de forma fija — aquí queda disponible para cualquier
    /// cliente que lo necesite.
    /// </summary>
    [ObservableProperty]
    private bool _imprimirPorLotes;

    /// <summary>
    /// True justo después de "Generar" (LPNs reservados en el grid, aún no guardados en
    /// Histórico): el botón principal cambia de texto a "Guardar" y de color a naranja.
    /// Vuelve a false en cuanto "Guardar" inserta los registros (o al cargar una hoja nueva).
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerarOGuardarCommand))]
    [NotifyPropertyChangedFor(nameof(TextoBotonPrincipal))]
    private bool _estaListoParaGuardar;

    [ObservableProperty]
    private string _estadoMensaje = "Listo. Carga un Excel para comenzar.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerarOGuardarCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImprimirCommand))]
    private bool _estaOcupado;

    [ObservableProperty]
    private double _progreso;

    [ObservableProperty]
    private byte[]? _imagenPreview;

    public string NombreArchivo => string.IsNullOrEmpty(RutaArchivo) ? "Sin archivo" : Path.GetFileName(RutaArchivo);

    public string ClienteActivoTexto => ClienteSeleccionado is null
        ? "Sin cliente identificado"
        : $"{ClienteSeleccionado.Nombre} ({ClienteSeleccionado.EstrategiaLpn})";

    /// <summary>"Generar" en reposo; "Guardar" (con fondo naranja, ver la vista) mientras hay LPNs generados pendientes de insertar en Histórico.</summary>
    public string TextoBotonPrincipal => EstaListoParaGuardar ? "Guardar" : "Generar";

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
            GenerarOGuardarCommand.NotifyCanExecuteChanged();
            ImprimirCommand.NotifyCanExecuteChanged();
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
            await ResolverPlantillaDinamicaAsync(hoja);

            var tabla = await Task.Run(() => LeerHojaComoDataTable(ruta, hoja));
            DatosExcel = tabla;
            VistaDatos = tabla.DefaultView;
            _reservaPendiente = null;
            EstaListoParaGuardar = false;
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

    private bool HayFilasSeleccionadas() =>
        DatosExcel is not null &&
        DatosExcel.Rows.Cast<DataRow>().Any(fila => fila["_Seleccionado"] is bool marcada && marcada);

    private bool HayFilasSinLpnEnSeleccion() =>
        DatosExcel is not null &&
        DatosExcel.Rows.Cast<DataRow>().Any(fila =>
            fila["_Seleccionado"] is bool marcada && marcada &&
            string.IsNullOrWhiteSpace(ObtenerValorColumna(fila, "LPN")));

    private bool HayLpnVisibleParaImprimir() =>
        DatosExcel is not null &&
        DatosExcel.Rows.Cast<DataRow>().Any(fila =>
            fila["_Seleccionado"] is bool marcada && marcada &&
            !string.IsNullOrWhiteSpace(ObtenerValorColumna(fila, "LPN")));

    /// <summary>
    /// Habilitado como "Generar" mientras la selección tenga alguna fila sin LPN todavía;
    /// una vez que "Generar" reservó folios para toda la selección (EstaListoParaGuardar),
    /// sigue habilitado pero pasa a actuar como "Guardar". Al terminar de guardar (o al venir
    /// de "Reimprimir Selección", donde todo ya tiene LPN), queda deshabilitado: no hay nada
    /// más que generar ni guardar para esa selección, y así se evita reservar folios de más.
    /// </summary>
    private bool PuedeGenerarOGuardar() =>
        !EstaOcupado && ClienteSeleccionado is not null && HayFilasSeleccionadas() &&
        (EstaListoParaGuardar || HayFilasSinLpnEnSeleccion());

    [RelayCommand(CanExecute = nameof(PuedeGenerarOGuardar))]
    private Task GenerarOGuardarAsync() => EstaListoParaGuardar ? GuardarAsync() : GenerarAsync();

    /// <summary>
    /// Botón "Generar": solo reserva folios (LPN) para las filas seleccionadas que aún no
    /// tienen uno y los muestra en el grid. No inserta nada en historico_impresiones todavía
    /// — eso lo hace "Guardar" — para que un folio reservado nunca quede "a medias" sin que
    /// el operador decida explícitamente confirmarlo.
    /// </summary>
    private async Task GenerarAsync()
    {
        if (DatosExcel is null || ClienteSeleccionado is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Solicitante) || string.IsNullOrWhiteSpace(Arribo))
        {
            EstadoMensaje = "Ingresa Solicitante y Arribo antes de generar.";
            return;
        }

        if (_services.Consecutivos is null)
        {
            EstadoMensaje = "Neon no está disponible: no se pueden reservar folios sin conexión.";
            return;
        }

        EstaOcupado = true;
        try
        {
            RevalidarFilas();
            var esMiniso = string.Equals(ClienteSeleccionado.EstrategiaLpn, "MINISO", StringComparison.OrdinalIgnoreCase);

            // Solo las filas seleccionadas que TODAVÍA no tienen LPN: si el operador ya
            // generó una parte de la selección y vuelve a ampliar la selección, "Generar"
            // no vuelve a pedir folios para las que ya tienen uno.
            var filas = DatosExcel.Rows.Cast<DataRow>()
                .Where(fila => fila["_Seleccionado"] is bool marcada && marcada &&
                                string.IsNullOrWhiteSpace(ObtenerValorColumna(fila, "LPN")))
                .ToList();

            if (filas.Count == 0)
            {
                EstadoMensaje = "No hay filas seleccionadas pendientes de generar LPN.";
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
                    fila["_YaGuardado"] = false;
                    consecutivoPorFila[fila] = consecutivo;
                }
            }

            _reservaPendiente = infoPorFila.Select(x => (x.Fila, x.Tipo, consecutivoPorFila[x.Fila])).ToList();
            EstaListoParaGuardar = true;
            EstadoMensaje = $"{filas.Count} LPN(s) generado(s). Revisa el grid y presiona 'Guardar' para registrarlos en Histórico.";

            if (filas.Count > 0)
            {
                await ActualizarPreviewAsync(filas[0]);
            }
        }
        catch (Exception ex)
        {
            EstadoMensaje = $"Error durante la generación: {ex.Message}";
        }
        finally
        {
            EstaOcupado = false;
            ImprimirCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Botón "Guardar": calcula y asigna TarimaActual/TarimaTotal (respetando "Impresión por
    /// lotes") para los folios que "Generar" acaba de reservar, e inserta los registros en
    /// historico_impresiones (Neon y caché local). Es el único punto del flujo que inserta.
    /// </summary>
    private async Task GuardarAsync()
    {
        if (DatosExcel is null || ClienteSeleccionado is null)
        {
            return;
        }

        if (_reservaPendiente is null || _reservaPendiente.Count == 0)
        {
            EstadoMensaje = "No hay LPNs generados pendientes de guardar.";
            EstaListoParaGuardar = false;
            return;
        }

        if (_services.Historico is null)
        {
            EstadoMensaje = "Neon no está disponible: no se puede guardar en Histórico sin conexión.";
            return;
        }

        EstaOcupado = true;
        try
        {
            var todasLasFilas = DatosExcel.Rows.Cast<DataRow>().ToList();
            var indiceAbsolutoPorFila = todasLasFilas
                .Select((fila, indice) => (fila, indice))
                .ToDictionary(x => x.fila, x => x.indice + 1);

            var filas = _reservaPendiente.Select(x => x.Fila).ToList();
            var esMyc = string.Equals(ClienteSeleccionado.Codigo, "MYC", StringComparison.OrdinalIgnoreCase);
            var total = filas.Count;
            var totalHojaCompleta = todasLasFilas.Count;
            var usarIndexadoPorLotes = esMyc || ImprimirPorLotes;
            var tarimaPorFila = CalcularTarimaPorFila(filas, indiceAbsolutoPorFila, total, totalHojaCompleta, usarIndexadoPorLotes);

            var registros = _reservaPendiente.Select(x => new HistoricoImpresionRecord(
                Lpn: ObtenerValorColumna(x.Fila, "LPN"),
                Consecutivo: x.Consecutivo,
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
                // CAJAS es texto (no numérico): algunos clientes (p. ej. AXO) capturan
                // valores no numéricos como "NV" junto con cantidades reales.
                Cajas: NuloSiVacio(ObtenerValorColumna(x.Fila, "CAJAS")),
                TarimaActual: tarimaPorFila[x.Fila].Indice,
                TarimaTotal: tarimaPorFila[x.Fila].Total,
                VariablesJson: JsonSerializer.Serialize(FilaADiccionario(x.Fila))))
                .ToList();

            await _services.Historico.InsertarLoteAsync(registros);
            await _services.CacheLocal.ReplicarHistoricoAsync(registros);

            foreach (var x in _reservaPendiente)
            {
                x.Fila["_YaGuardado"] = true;
                x.Fila["_TarimaActual"] = tarimaPorFila[x.Fila].Indice;
                x.Fila["_TarimaTotal"] = tarimaPorFila[x.Fila].Total;
                x.Fila["_Consecutivo"] = x.Consecutivo;
            }

            EstadoMensaje = $"{total} etiqueta(s) guardadas en Histórico. Usa 'Imprimir' para enviarlas a la impresora.";
            _reservaPendiente = null;
            EstaListoParaGuardar = false;
            VistaDatos = DatosExcel.DefaultView;
        }
        catch (Exception ex)
        {
            EstadoMensaje = $"Error guardando en Histórico: {ex.Message}";
        }
        finally
        {
            EstaOcupado = false;
            ImprimirCommand.NotifyCanExecuteChanged();
        }
    }

    private bool PuedeImprimir() =>
        !EstaOcupado && !string.IsNullOrWhiteSpace(ImpresoraSeleccionada) && HayLpnVisibleParaImprimir();

    /// <summary>
    /// Botón "Imprimir": ya no reserva folios nuevos — imprime el LPN visible de cada fila
    /// seleccionada (las que no tienen LPN se omiten). Antes de imprimir, recalcula
    /// TarimaActual/TarimaTotal según la selección vigente y "Impresión por lotes" (el
    /// operador pudo haber partido el lote respecto a lo guardado originalmente); si la fila
    /// ya existía en historico_impresiones (guardada antes, o cargada desde "Reimprimir
    /// Selección") y el recalculo dio un valor distinto al ya grabado, corrige
    /// tarima_actual/tarima_total en Neon y en la caché local antes de imprimir, para que la
    /// base de datos refleje exactamente cómo salió la etiqueta física.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeImprimir))]
    private async Task ImprimirAsync()
    {
        if (DatosExcel is null)
        {
            return;
        }

        EstaOcupado = true;
        Progreso = 0;
        try
        {
            var todasLasFilas = DatosExcel.Rows.Cast<DataRow>().ToList();
            var indiceAbsolutoPorFila = todasLasFilas
                .Select((fila, indice) => (fila, indice))
                .ToDictionary(x => x.fila, x => x.indice + 1);

            var filas = todasLasFilas
                .Where(fila => fila["_Seleccionado"] is bool marcada && marcada &&
                               !string.IsNullOrWhiteSpace(ObtenerValorColumna(fila, "LPN")))
                .ToList();

            if (filas.Count == 0)
            {
                EstadoMensaje = "No hay LPNs visibles para imprimir: genera folios primero o revisa la selección.";
                return;
            }

            var esMyc = string.Equals(ClienteSeleccionado?.Codigo, "MYC", StringComparison.OrdinalIgnoreCase);
            var total = filas.Count;
            var totalHojaCompleta = todasLasFilas.Count;
            var usarIndexadoPorLotes = esMyc || ImprimirPorLotes;
            var tarimaPorFila = CalcularTarimaPorFila(filas, indiceAbsolutoPorFila, total, totalHojaCompleta, usarIndexadoPorLotes);

            var errores = 0;
            var tarimasCorregidas = 0;
            var esImpresoraVirtual = string.Equals(ImpresoraSeleccionada, ImpresoraVirtualNombre, StringComparison.Ordinal);
            var auditoriaVirtual = esImpresoraVirtual ? new List<AuditoriaEtiqueta>(total) : null;

            for (var i = 0; i < filas.Count; i++)
            {
                var fila = filas[i];
                var (indiceNuevo, totalNuevo) = tarimaPorFila[fila];
                var lpn = ObtenerValorColumna(fila, "LPN");

                // REGLA CRÍTICA: corrige tarima_actual/tarima_total en la BD si esta fila ya
                // estaba guardada y el recalculo (por selección o "Impresión por lotes") dio
                // un valor distinto al ya grabado.
                var yaGuardado = fila["_YaGuardado"] is bool yg && yg;
                if (yaGuardado)
                {
                    var actualPrevio = fila["_TarimaActual"] as int?;
                    var totalPrevio = fila["_TarimaTotal"] as int?;
                    if (actualPrevio != indiceNuevo || totalPrevio != totalNuevo)
                    {
                        if (_services.Historico is not null)
                        {
                            try
                            {
                                await _services.Historico.ActualizarTarimaAsync(lpn, indiceNuevo, totalNuevo);
                            }
                            catch (Exception ex)
                            {
                                EstadoMensaje = $"No se pudo corregir la tarima en Neon para '{lpn}': {ex.Message}";
                            }
                        }

                        await _services.CacheLocal.ActualizarTarimaAsync(lpn, indiceNuevo, totalNuevo);
                        fila["_TarimaActual"] = indiceNuevo;
                        fila["_TarimaTotal"] = totalNuevo;
                        tarimasCorregidas++;
                    }
                }

                var consecutivo = fila["_Consecutivo"] as int?;
                var zpl = GenerarZplParaFila(fila, consecutivo, indiceNuevo, totalNuevo);

                if (esImpresoraVirtual)
                {
                    // Modo auditoría: nunca toca el spooler real; renderiza vía Labelary
                    // para revisión visual.
                    var resultadoPreview = await _services.Preview.GenerarVistaPreviaAsync(zpl);
                    auditoriaVirtual!.Add(new AuditoriaEtiqueta(
                        lpn,
                        resultadoPreview.Exito ? resultadoPreview.ImagenPng : null,
                        resultadoPreview.Exito ? null : resultadoPreview.Error));
                }
                else
                {
                    var resultado = await _services.Impresoras.EncolarAsync(ImpresoraSeleccionada!, zpl, $"LPN {lpn}");
                    if (!resultado.Exito)
                    {
                        errores++;
                    }
                }

                Progreso = (i + 1) / (double)total;
            }

            EstadoMensaje = esImpresoraVirtual
                ? $"Impresión (modo virtual) terminada: {total} etiqueta(s), {tarimasCorregidas} tarima(s) corregida(s) en la BD."
                : $"Impresión terminada: {total} etiqueta(s), {errores} error(es), {tarimasCorregidas} tarima(s) corregida(s) en la BD.";

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
            EstadoMensaje = $"Error durante la impresión: {ex.Message}";
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    private static Dictionary<DataRow, (int Indice, int Total)> CalcularTarimaPorFila(
        IReadOnlyList<DataRow> filas,
        IReadOnlyDictionary<DataRow, int> indiceAbsolutoPorFila,
        int total,
        int totalHojaCompleta,
        bool usarIndexadoPorLotes)
    {
        var resultado = new Dictionary<DataRow, (int, int)>();
        for (var i = 0; i < filas.Count; i++)
        {
            resultado[filas[i]] = usarIndexadoPorLotes
                ? (i + 1, total)
                : (indiceAbsolutoPorFila[filas[i]], totalHojaCompleta);
        }

        return resultado;
    }

    /// <summary>
    /// Carga en el grid registros ya existentes en historico_impresiones (viene de
    /// "Reimprimir Selección" en la pestaña Histórico), con su LPN, TarimaActual/TarimaTotal
    /// e Id ya asignados, para que el operador los reimprima tal cual con "Imprimir", o los
    /// reparta de otra forma con la selección / "Impresión por lotes" (disparando la regla
    /// crítica de recálculo y UPDATE de tarima al imprimir).
    /// </summary>
    public async Task CargarRegistrosDesdeHistoricoAsync(IReadOnlyList<HistoricoImpresionRecord> registros)
    {
        if (registros.Count == 0)
        {
            return;
        }

        EstaOcupado = true;
        try
        {
            RutaArchivo = null;
            OnPropertyChanged(nameof(NombreArchivo));
            Hojas.Clear();
            HojaSeleccionada = null;

            var clienteCodigo = registros[0].Cliente;
            var cliente = clienteCodigo is null
                ? null
                : Clientes.FirstOrDefault(c => string.Equals(c.Codigo, clienteCodigo, StringComparison.OrdinalIgnoreCase));

            ClienteSeleccionado = cliente;
            RequiereSeleccionManualDeCliente = cliente is null;

            _plantillaDinamica = null;
            if (clienteCodigo is not null)
            {
                // plantillas_zpl se siembra con hoja_excel = código de cliente, así que el
                // propio código sirve como "hoja" para resolver la plantilla dinámica aquí,
                // donde no hay una hoja de Excel real de por medio.
                await ResolverPlantillaDinamicaAsync(clienteCodigo);
            }

            var tabla = ConstruirTablaDesdeHistorico(registros);
            DatosExcel = tabla;
            VistaDatos = tabla.DefaultView;

            Arribo = registros[0].Arribo;
            Solicitante = registros[0].Solicitante;

            _reservaPendiente = null;
            EstaListoParaGuardar = false;

            EstadoMensaje = $"{registros.Count} registro(s) cargados desde Histórico. Usa 'Imprimir' para reimprimirlos tal cual, " +
                "o ajusta la selección / 'Impresión por lotes' para partir el lote (se corrige la tarima en la base de datos).";

            if (tabla.Rows.Count > 0)
            {
                await ActualizarPreviewAsync(tabla.Rows[0]);
            }
            else
            {
                ImagenPreview = null;
            }
        }
        finally
        {
            EstaOcupado = false;
            GenerarOGuardarCommand.NotifyCanExecuteChanged();
            ImprimirCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Reconstruye un DataTable "tipo Excel" a partir de registros de historico_impresiones:
    /// une las columnas de VariablesJson de todos los registros (insensible a mayúsculas), y
    /// agrega las columnas ocultas de control, ya marcadas como guardadas (_YaGuardado=true)
    /// con su TarimaActual/TarimaTotal/Consecutivo originales.
    /// </summary>
    private static DataTable ConstruirTablaDesdeHistorico(IReadOnlyList<HistoricoImpresionRecord> registros)
    {
        var nombresColumnas = new List<string>();
        var filasDatos = new List<Dictionary<string, string?>>();

        foreach (var registro in registros)
        {
            var deserializado = string.IsNullOrEmpty(registro.VariablesJson)
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(registro.VariablesJson);
            var datos = new Dictionary<string, string?>(deserializado ?? new Dictionary<string, string?>(), StringComparer.OrdinalIgnoreCase);

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
                datos.TryAdd("CANTIDAD", registro.Cantidad.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (registro.Cajas is not null)
            {
                datos.TryAdd("CAJAS", registro.Cajas);
            }

            filasDatos.Add(datos);
            foreach (var columna in datos.Keys)
            {
                if (!nombresColumnas.Contains(columna, StringComparer.OrdinalIgnoreCase))
                {
                    nombresColumnas.Add(columna);
                }
            }
        }

        var tabla = new DataTable();
        foreach (var nombre in nombresColumnas)
        {
            tabla.Columns.Add(nombre, typeof(string));
        }

        tabla.Columns.Add("_EsValido", typeof(bool));
        tabla.Columns.Add("_MensajeError", typeof(string));
        tabla.Columns.Add("_Seleccionado", typeof(bool));
        tabla.Columns.Add("_YaGuardado", typeof(bool));
        tabla.Columns.Add("_TarimaActual", typeof(int));
        tabla.Columns.Add("_TarimaTotal", typeof(int));
        tabla.Columns.Add("_Consecutivo", typeof(int));

        for (var i = 0; i < registros.Count; i++)
        {
            var nuevaFila = tabla.NewRow();
            foreach (var nombre in nombresColumnas)
            {
                nuevaFila[nombre] = filasDatos[i].TryGetValue(nombre, out var valor) ? (object?)valor ?? DBNull.Value : DBNull.Value;
            }

            nuevaFila["_EsValido"] = true;
            nuevaFila["_MensajeError"] = string.Empty;
            nuevaFila["_Seleccionado"] = true;
            nuevaFila["_YaGuardado"] = true;
            nuevaFila["_TarimaActual"] = registros[i].TarimaActual ?? 1;
            nuevaFila["_TarimaTotal"] = registros[i].TarimaTotal ?? registros.Count;
            if (registros[i].Consecutivo.HasValue)
            {
                nuevaFila["_Consecutivo"] = registros[i].Consecutivo!.Value;
            }

            tabla.Rows.Add(nuevaFila);
        }

        return tabla;
    }

    private async Task ActualizarPreviewAsync(DataRow fila)
    {
        if (_plantillaDinamica is null && ClienteSeleccionado?.PlantillaZpl is null)
        {
            ImagenPreview = null;
            return;
        }

        // Vista previa: el consecutivo real del folio (LPN) todavía no existe (se reserva
        // recién al generar), así que {{CONSECUTIVO}} queda en blanco hasta ese momento.
        var zpl = GenerarZplParaFila(fila, consecutivo: null, indiceActual: 1, totalFilas: 1);
        var resultado = await _services.Preview.GenerarVistaPreviaAsync(zpl);

        ImagenPreview = resultado.Exito ? resultado.ImagenPng : null;
        if (!resultado.Exito)
        {
            EstadoMensaje = $"Vista previa no disponible: {resultado.Error}";
        }
    }

    /// <summary>
    /// Resuelve el ZPL de una fila: si hay una plantilla administrada en vivo
    /// (plantillas_zpl) activa para la hoja seleccionada, la usa (placeholders "{{TAG}}");
    /// si no, cae al mecanismo existente de cat_clientes (placeholders "{PLACEHOLDER}" +
    /// mapeo_columnas), para no romper compatibilidad con clientes aún no migrados.
    /// </summary>
    private string GenerarZplParaFila(DataRow fila, int? consecutivo, int indiceActual, int totalFilas)
    {
        var datos = FilaADiccionario(fila);

        if (_plantillaDinamica is not null)
        {
            var lpn = ObtenerValorColumna(fila, "LPN");
            return ZplPlantillaDinamicaEngine.Generar(
                _plantillaDinamica.CodigoZpl,
                datos,
                lpn: string.IsNullOrEmpty(lpn) ? null : lpn,
                consecutivo: consecutivo,
                tarimaActual: indiceActual,
                tarimaTotal: totalFilas,
                arribo: Arribo,
                solicitante: Solicitante);
        }

        return ZplTemplateEngine.Generar(
            ClienteSeleccionado?.PlantillaZpl ?? string.Empty,
            ClienteSeleccionado?.MapeoColumnas ?? new Dictionary<string, string>(),
            datos,
            indiceActual,
            totalFilas);
    }

    /// <summary>
    /// Busca en plantillas_zpl (Neon, con fallback a la caché local si no hay conexión) una
    /// plantilla activa enlazada al nombre exacto de la hoja seleccionada. Si no existe
    /// ninguna, _plantillaDinamica queda en null y el resto del flujo usa el mecanismo
    /// existente sin ningún cambio de comportamiento.
    /// </summary>
    private async Task ResolverPlantillaDinamicaAsync(string hoja)
    {
        try
        {
            _plantillaDinamica = _services.Plantillas is not null
                ? await _services.Plantillas.ObtenerPorHojaAsync(hoja)
                : await _services.CacheLocal.ObtenerPlantillaPorHojaAsync(hoja);
        }
        catch
        {
            _plantillaDinamica = await _services.CacheLocal.ObtenerPlantillaPorHojaAsync(hoja);
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
        tabla.Columns.Add("_YaGuardado", typeof(bool));
        tabla.Columns.Add("_TarimaActual", typeof(int));
        tabla.Columns.Add("_TarimaTotal", typeof(int));
        tabla.Columns.Add("_Consecutivo", typeof(int));

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
            nuevaFila["_YaGuardado"] = false; // aún no existe en historico_impresiones
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
