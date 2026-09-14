using System.Globalization;
using Microsoft.Data.SqlClient;
using Zps.Data.Models.Wms;

namespace Zps.Data;

/// <summary>
/// Puente de SOLO LECTURA hacia HighJump/AAD (SQL Server, 192.168.100.76) para alimentar el
/// módulo de paletización y recibo digital con datos reales del WMS (órdenes, líneas de PO,
/// empleados) en vez de capturarlos a mano. Todas las consultas usan WITH (NOLOCK): es un
/// sistema de producción ajeno a ZPS y no se le debe imponer ningún bloqueo transaccional.
///
/// Nombres de columna verificados en vivo contra el esquema real (INFORMATION_SCHEMA.COLUMNS)
/// vía AadWmsServiceIntegrationTests: t_po_master coincidió exactamente con la especificación
/// original, pero t_employee y t_po_detail no — ver los comentarios en cada método. Todas las
/// consultas usan un CommandTimeout corto (<see cref="TimeoutSegundos"/>) para que una caída
/// o saturación del WMS no cuelgue la UI de ZPS indefinidamente.
/// </summary>
public sealed class AadWmsService
{
    // PIN temporal fijo mientras se define la autenticación definitiva del módulo de
    // paletización (ver Fase 1.1): cualquier empleado que exista en t_employee con este PIN
    // puede iniciar sesión como operario.
    private const string PinTemporal = "1090";
    private const int TimeoutSegundos = 5;

    /// <summary>Tope aplicado a BuscarInventarioAsync cuando no hay ningún filtro de texto, sin importar LimiteMaximo.</summary>
    private const int SalvaguardaSinFiltros = 500;

    private readonly string _connectionString;

    public AadWmsService(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>Sondeo de conectividad simple (SELECT 1), sin depender de ninguna tabla del WMS.</summary>
    public async Task<bool> ProbarConexionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT 1;", connection) { CommandTimeout = TimeoutSegundos };
        await command.ExecuteScalarAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// t_employee.employee_id es un id interno (int) ajeno al login; el campo que el
    /// operario realmente teclea en HighJump es t_employee.id (nvarchar). No existe
    /// separación de nombre/apellido, solo t_employee.name.
    /// </summary>
    public async Task<WmsEmployeeModel?> ValidarCredencialesAsync(
        string usuario, string pin, CancellationToken cancellationToken = default)
    {
        if (pin != PinTemporal)
        {
            return null;
        }

        const string sql = """
            SELECT id, name, status
            FROM t_employee WITH (NOLOCK)
            WHERE id = @usuario;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = TimeoutSegundos };
        command.Parameters.AddWithValue("@usuario", usuario);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? LeerEmpleado(reader) : null;
    }

    public async Task<IReadOnlyList<WmsPoHeaderModel>> ObtenerOrdenesAbiertasAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT po_number, client_code, vendor_code, create_date, status
            FROM t_po_master WITH (NOLOCK)
            WHERE status = 'O'
            ORDER BY create_date DESC;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = TimeoutSegundos };

        var resultado = new List<WmsPoHeaderModel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            resultado.Add(LeerPoHeader(reader));
        }

        return resultado;
    }

    /// <summary>
    /// t_po_detail.line_number es nvarchar (no numérico) en el esquema real, y la columna de
    /// unidad de medida se llama order_uom (no uom): se ordena por su conversión numérica
    /// para no ordenar "10" antes que "2" como texto.
    /// </summary>
    public async Task<IReadOnlyList<WmsPoDetailModel>> ObtenerDetalleOrdenAsync(
        string poNumber, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT d.po_number, d.line_number, d.item_number, d.qty, d.order_uom AS uom
            FROM t_po_detail d WITH (NOLOCK)
            INNER JOIN t_po_master m WITH (NOLOCK) ON m.po_number = d.po_number
            WHERE d.po_number = @po_number
            ORDER BY TRY_CAST(d.line_number AS INT), d.line_number;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = TimeoutSegundos };
        command.Parameters.AddWithValue("@po_number", poNumber);

        var resultado = new List<WmsPoDetailModel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            resultado.Add(LeerPoDetail(reader));
        }

        return resultado;
    }

    /// <summary>
    /// Busca inventario en t_stored_item, unido a t_hu_master por hu_id (la unidad de
    /// manejo/HU de HighJump ES la tarima física, de ahí que Lpn salga de t_hu_master.hu_id)
    /// y, con LEFT JOIN, a t_item_master por item_number (+wh_id, para no toparse con
    /// choques de item_number entre clientes) — de ahí salen Descripcion y Cliente, ya que
    /// ni t_stored_item ni t_hu_master tienen client_code propio. CantidadRetenida sale de
    /// t_stored_item.unavailable_qty (cantidad reservada/no disponible para surtir).
    ///
    /// filtros.LimiteMaximo controla el TOP: si viene nulo/0 y hay al menos un filtro de
    /// texto (Lpn/Sku/Lote/Ubicacion), se omite el TOP por completo para traer el lote
    /// filtrado sin recortar; sin ningún filtro de texto, siempre se aplica una salvaguarda
    /// dura de 500 filas para no descargar el inventario completo del WMS por accidente.
    /// </summary>
    public async Task<IReadOnlyList<InventarioItemModel>> BuscarInventarioAsync(
        InventarioFiltrosModel filtros, CancellationToken cancellationToken = default)
    {
        var hayFiltroTexto = !string.IsNullOrWhiteSpace(filtros.Lpn)
            || !string.IsNullOrWhiteSpace(filtros.Sku)
            || !string.IsNullOrWhiteSpace(filtros.Lote)
            || !string.IsNullOrWhiteSpace(filtros.Ubicacion);

        int? topEfectivo = filtros.LimiteMaximo is null or 0
            ? (hayFiltroTexto ? null : SalvaguardaSinFiltros)
            : filtros.LimiteMaximo;

        var clausulaTop = topEfectivo.HasValue ? "TOP (@limite) " : string.Empty;

        var sql = $"""
            SELECT {clausulaTop}hu.hu_id AS lpn,
                si.item_number AS sku,
                im.description AS descripcion,
                si.lot_number AS lote,
                hu.location_id AS ubicacion,
                im.client_code AS cliente,
                si.actual_qty AS cantidad_total,
                si.unavailable_qty AS cantidad_retenida,
                (si.actual_qty - si.unavailable_qty) AS cantidad_disponible,
                si.expiration_date AS caducidad,
                hu.status AS estatus_tarima
            FROM t_stored_item si WITH (NOLOCK)
            INNER JOIN t_hu_master hu WITH (NOLOCK) ON hu.hu_id = si.hu_id
            LEFT JOIN t_item_master im WITH (NOLOCK) ON im.item_number = si.item_number AND im.wh_id = si.wh_id
            WHERE (@lpn IS NULL OR hu.hu_id LIKE '%' + @lpn + '%')
              AND (@sku IS NULL OR si.item_number LIKE '%' + @sku + '%')
              AND (@lote IS NULL OR si.lot_number LIKE '%' + @lote + '%')
              AND (@ubicacion IS NULL OR hu.location_id LIKE '%' + @ubicacion + '%')
              AND (@cliente IS NULL OR im.client_code = @cliente)
              AND (@soloDisponibles = 0 OR (si.actual_qty - si.unavailable_qty) > 0)
            ORDER BY hu.hu_id, si.sequence;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = TimeoutSegundos };
        if (topEfectivo.HasValue)
        {
            command.Parameters.AddWithValue("@limite", topEfectivo.Value);
        }

        AgregarFiltroTexto(command, "@lpn", filtros.Lpn);
        AgregarFiltroTexto(command, "@sku", filtros.Sku);
        AgregarFiltroTexto(command, "@lote", filtros.Lote);
        AgregarFiltroTexto(command, "@ubicacion", filtros.Ubicacion);
        AgregarFiltroTexto(command, "@cliente", filtros.Cliente);
        command.Parameters.AddWithValue("@soloDisponibles", filtros.SoloDisponibles);

        var resultado = new List<InventarioItemModel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            resultado.Add(LeerInventarioItem(reader));
        }

        return resultado;
    }

    /// <summary>Un filtro de texto vacío/en blanco se manda como NULL para que el WHERE lo ignore.</summary>
    private static void AgregarFiltroTexto(SqlCommand command, string nombre, string? valor)
    {
        command.Parameters.AddWithValue(nombre, string.IsNullOrWhiteSpace(valor) ? DBNull.Value : valor.Trim());
    }

    private static InventarioItemModel LeerInventarioItem(SqlDataReader reader) => new(
        Lpn: reader.GetString(reader.GetOrdinal("lpn")),
        Sku: reader.GetString(reader.GetOrdinal("sku")),
        Descripcion: LeerStringONull(reader, "descripcion"),
        Lote: LeerStringONull(reader, "lote"),
        Ubicacion: LeerStringONull(reader, "ubicacion") ?? string.Empty,
        Cliente: LeerStringONull(reader, "cliente") ?? string.Empty,
        CantidadTotal: Convert.ToInt32(reader.GetValue(reader.GetOrdinal("cantidad_total"))),
        CantidadRetenida: Convert.ToInt32(reader.GetValue(reader.GetOrdinal("cantidad_retenida"))),
        CantidadDisponible: Convert.ToInt32(reader.GetValue(reader.GetOrdinal("cantidad_disponible"))),
        Caducidad: reader.IsDBNull(reader.GetOrdinal("caducidad")) ? null : reader.GetDateTime(reader.GetOrdinal("caducidad")),
        EstatusTarima: LeerStringONull(reader, "estatus_tarima")?.Trim() ?? string.Empty);

    private static WmsEmployeeModel LeerEmpleado(SqlDataReader reader) => new(
        EmployeeId: reader.GetString(reader.GetOrdinal("id")),
        FullName: LeerStringONull(reader, "name"),
        Status: LeerStringONull(reader, "status"));

    private static WmsPoHeaderModel LeerPoHeader(SqlDataReader reader) => new(
        PoNumber: reader.GetString(reader.GetOrdinal("po_number")),
        ClientCode: LeerStringONull(reader, "client_code"),
        VendorCode: LeerStringONull(reader, "vendor_code"),
        CreateDate: reader.GetDateTime(reader.GetOrdinal("create_date")),
        Status: reader.GetString(reader.GetOrdinal("status")));

    private static WmsPoDetailModel LeerPoDetail(SqlDataReader reader) => new(
        PoNumber: reader.GetString(reader.GetOrdinal("po_number")),
        LineNumber: int.Parse(reader.GetString(reader.GetOrdinal("line_number")), CultureInfo.InvariantCulture),
        ItemNumber: reader.GetString(reader.GetOrdinal("item_number")),
        Qty: Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("qty"))),
        Uom: LeerStringONull(reader, "uom"));

    private static string? LeerStringONull(SqlDataReader reader, string columna)
    {
        var ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
