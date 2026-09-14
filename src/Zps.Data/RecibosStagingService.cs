using Npgsql;
using Zps.Data.Models.Recibos;

namespace Zps.Data;

/// <summary>
/// Cabecera de recibos en proceso de paletización digital (recibos_staging): alta,
/// consultas por status/PO, cambios de status (con auditoría en
/// recibos_staging_cambios_log) y recálculo de los totales agregados a partir de las
/// líneas activas de recibos_staging_detalle.
/// </summary>
public sealed class RecibosStagingService
{
    private readonly NpgsqlDataSource _dataSource;

    public RecibosStagingService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<ReciboStagingModel> CrearReciboAsync(
        ReciboStagingModel cabecera, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO recibos_staging (po_number, client_code, vendor_code, status, solicitante, total_piezas_esperadas, total_piezas_paletizadas, total_tarimas)
            VALUES (@po_number, @client_code, @vendor_code, @status, @solicitante, @total_piezas_esperadas, @total_piezas_paletizadas, @total_tarimas)
            RETURNING id, po_number, client_code, vendor_code, status, solicitante, total_piezas_esperadas, total_piezas_paletizadas, total_tarimas, creado_en, actualizado_en;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("po_number", cabecera.PoNumber);
        command.Parameters.AddWithValue("client_code", cabecera.ClientCode);
        command.Parameters.AddWithValue("vendor_code", (object?)cabecera.VendorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("status", cabecera.Status);
        command.Parameters.AddWithValue("solicitante", (object?)cabecera.Solicitante ?? DBNull.Value);
        command.Parameters.AddWithValue("total_piezas_esperadas", cabecera.TotalPiezasEsperadas);
        command.Parameters.AddWithValue("total_piezas_paletizadas", cabecera.TotalPiezasPaletizadas);
        command.Parameters.AddWithValue("total_tarimas", cabecera.TotalTarimas);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return LeerRegistro(reader);
    }

    public async Task<IReadOnlyList<ReciboStagingModel>> ObtenerRecibosPorStatusAsync(
        string status, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, po_number, client_code, vendor_code, status, solicitante, total_piezas_esperadas, total_piezas_paletizadas, total_tarimas, creado_en, actualizado_en
            FROM recibos_staging
            WHERE status = @status
            ORDER BY creado_en DESC;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("status", status);

        var resultado = new List<ReciboStagingModel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            resultado.Add(LeerRegistro(reader));
        }

        return resultado;
    }

    /// <summary>
    /// Busca el recibo más reciente para un número de PO. po_number no tiene restricción
    /// UNIQUE en Neon (un mismo PO puede reintentarse tras un recibo CANCELADO), así que
    /// ante varias coincidencias se devuelve la más reciente por creado_en.
    /// </summary>
    public async Task<ReciboStagingModel?> ObtenerReciboPorPoAsync(
        string poNumber, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, po_number, client_code, vendor_code, status, solicitante, total_piezas_esperadas, total_piezas_paletizadas, total_tarimas, creado_en, actualizado_en
            FROM recibos_staging
            WHERE po_number = @po_number
            ORDER BY creado_en DESC
            LIMIT 1;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("po_number", poNumber);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? LeerRegistro(reader) : null;
    }

    /// <summary>
    /// Cambia el status de un recibo y registra el cambio en recibos_staging_cambios_log
    /// (entidad 'RECIBO_CABECERA', campo 'status'), para conservar el historial completo del
    /// flujo BORRADOR -> EN_PROCESO -> CONFIRMADO -> IMPRESO / CANCELADO. No hace nada si el
    /// status nuevo es igual al actual (evita una entrada de auditoría sin cambio real).
    /// </summary>
    public async Task ActualizarStatusAsync(
        long reciboId, string nuevoStatus, string usuario, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        string statusAnterior;
        await using (var comandoActual = new NpgsqlCommand(
            "SELECT status FROM recibos_staging WHERE id = @id FOR UPDATE;", connection, transaction))
        {
            comandoActual.Parameters.AddWithValue("id", reciboId);
            var resultado = await comandoActual.ExecuteScalarAsync(cancellationToken);
            if (resultado is null)
            {
                throw new InvalidOperationException($"No existe el recibo con id {reciboId} en recibos_staging.");
            }

            statusAnterior = (string)resultado;
        }

        if (statusAnterior == nuevoStatus)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        await using (var comandoUpdate = new NpgsqlCommand(
            "UPDATE recibos_staging SET status = @status, actualizado_en = now() WHERE id = @id;", connection, transaction))
        {
            comandoUpdate.Parameters.AddWithValue("status", nuevoStatus);
            comandoUpdate.Parameters.AddWithValue("id", reciboId);
            await comandoUpdate.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var comandoLog = new NpgsqlCommand(
            """
            INSERT INTO recibos_staging_cambios_log (recibo_id, entidad_modificada, campo_modificado, valor_anterior, valor_nuevo, usuario, fecha_cambio)
            VALUES (@recibo_id, 'RECIBO_CABECERA', 'status', @anterior, @nuevo, @usuario, now());
            """, connection, transaction))
        {
            comandoLog.Parameters.AddWithValue("recibo_id", reciboId);
            comandoLog.Parameters.AddWithValue("anterior", statusAnterior);
            comandoLog.Parameters.AddWithValue("nuevo", nuevoStatus);
            comandoLog.Parameters.AddWithValue("usuario", usuario);
            await comandoLog.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Recalcula total_piezas_paletizadas (suma de cantidad_piezas) y total_tarimas (conteo
    /// de consecutivo_tarima distintos, ya que un mismo pallet físico puede tener varias
    /// líneas) a partir de las líneas activas de recibos_staging_detalle. Abre su propia
    /// conexión/transacción.
    /// </summary>
    public async Task ActualizarTotalesAsync(long reciboId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ActualizarTotalesAsync(reciboId, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Igual que <see cref="ActualizarTotalesAsync(long, CancellationToken)"/>, pero
    /// reutilizando una conexión/transacción ya abierta por el llamador — usado por
    /// RecibosStagingDetalleService para recalcular los totales de la cabecera dentro de la
    /// misma transacción en la que se agregó, editó o dio de baja una tarima.
    /// </summary>
    public async Task ActualizarTotalesAsync(
        long reciboId, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE recibos_staging
            SET total_piezas_paletizadas = sub.piezas,
                total_tarimas = sub.tarimas,
                actualizado_en = now()
            FROM (
                SELECT
                    COALESCE(SUM(cantidad_piezas), 0)::int AS piezas,
                    COUNT(DISTINCT consecutivo_tarima)::int AS tarimas
                FROM recibos_staging_detalle
                WHERE recibo_id = @recibo_id AND activo = true
            ) sub
            WHERE recibos_staging.id = @recibo_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("recibo_id", reciboId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Historial completo de auditoría (cabecera + tarimas) de un recibo, más reciente primero.</summary>
    public async Task<IReadOnlyList<ReciboCambioLogModel>> ObtenerCambiosLogAsync(
        long reciboId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, recibo_id, detalle_id, entidad_modificada, campo_modificado, valor_anterior, valor_nuevo, motivo_cambio, usuario, fecha_cambio
            FROM recibos_staging_cambios_log
            WHERE recibo_id = @recibo_id
            ORDER BY fecha_cambio DESC;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("recibo_id", reciboId);

        var resultado = new List<ReciboCambioLogModel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            resultado.Add(new ReciboCambioLogModel(
                Id: reader.GetInt64(reader.GetOrdinal("id")),
                ReciboId: reader.GetInt64(reader.GetOrdinal("recibo_id")),
                DetalleId: reader.IsDBNull(reader.GetOrdinal("detalle_id")) ? null : reader.GetInt64(reader.GetOrdinal("detalle_id")),
                EntidadModificada: reader.GetString(reader.GetOrdinal("entidad_modificada")),
                CampoModificado: reader.GetString(reader.GetOrdinal("campo_modificado")),
                ValorAnterior: reader.GetStringOrNull("valor_anterior"),
                ValorNuevo: reader.GetStringOrNull("valor_nuevo"),
                MotivoCambio: reader.GetStringOrNull("motivo_cambio"),
                Usuario: reader.GetString(reader.GetOrdinal("usuario")),
                FechaCambio: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("fecha_cambio"))));
        }

        return resultado;
    }

    private static ReciboStagingModel LeerRegistro(NpgsqlDataReader reader) => new(
        Id: reader.GetInt64(reader.GetOrdinal("id")),
        PoNumber: reader.GetString(reader.GetOrdinal("po_number")),
        ClientCode: reader.GetString(reader.GetOrdinal("client_code")),
        VendorCode: reader.GetStringOrNull("vendor_code"),
        Status: reader.GetString(reader.GetOrdinal("status")),
        Solicitante: reader.GetStringOrNull("solicitante"),
        TotalPiezasEsperadas: reader.GetInt32(reader.GetOrdinal("total_piezas_esperadas")),
        TotalPiezasPaletizadas: reader.GetInt32(reader.GetOrdinal("total_piezas_paletizadas")),
        TotalTarimas: reader.GetInt32(reader.GetOrdinal("total_tarimas")),
        CreadoEn: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("creado_en")),
        ActualizadoEn: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("actualizado_en")));
}
