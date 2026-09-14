using Npgsql;
using Zps.Data.Models.Recibos;

namespace Zps.Data;

/// <summary>
/// Líneas de detalle (tarimas virtuales) de un recibo en proceso de paletización
/// (recibos_staging_detalle), con auditoría de cada edición o baja en
/// recibos_staging_cambios_log — para poder corregir errores humanos (LPN, placas, SKU,
/// lote, cantidades, anulaciones) sin perder trazabilidad. Cada alta, edición o baja
/// lógica recalcula los totales de la cabecera (RecibosStagingService.ActualizarTotalesAsync)
/// dentro de la misma transacción.
/// </summary>
public sealed class RecibosStagingDetalleService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly RecibosStagingService _stagingService;

    public RecibosStagingDetalleService(NpgsqlDataSource dataSource, RecibosStagingService stagingService)
    {
        _dataSource = dataSource;
        _stagingService = stagingService;
    }

    public async Task<ReciboStagingDetalleModel> AgregarTarimaAsync(
        ReciboStagingDetalleModel detalle, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        ReciboStagingDetalleModel insertado;
        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO recibos_staging_detalle (recibo_id, item_number, descripcion, lote, atributo, cantidad_piezas, cantidad_cajas, es_pnc, hu_id, tipo_tarima, consecutivo_tarima, activo, creado_por, modificado_por)
            VALUES (@recibo_id, @item_number, @descripcion, @lote, @atributo, @cantidad_piezas, @cantidad_cajas, @es_pnc, @hu_id, @tipo_tarima, @consecutivo_tarima, @activo, @creado_por, @modificado_por)
            RETURNING id, recibo_id, item_number, descripcion, lote, atributo, cantidad_piezas, cantidad_cajas, es_pnc, hu_id, tipo_tarima, consecutivo_tarima, activo, creado_por, modificado_por, creado_en, actualizado_en;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("recibo_id", detalle.ReciboId);
            command.Parameters.AddWithValue("item_number", detalle.ItemNumber);
            command.Parameters.AddWithValue("descripcion", (object?)detalle.Descripcion ?? DBNull.Value);
            command.Parameters.AddWithValue("lote", (object?)detalle.Lote ?? DBNull.Value);
            command.Parameters.AddWithValue("atributo", (object?)detalle.Atributo ?? DBNull.Value);
            command.Parameters.AddWithValue("cantidad_piezas", detalle.CantidadPiezas);
            command.Parameters.AddWithValue("cantidad_cajas", (object?)detalle.CantidadCajas ?? DBNull.Value);
            command.Parameters.AddWithValue("es_pnc", detalle.EsPnc);
            command.Parameters.AddWithValue("hu_id", (object?)detalle.HuId ?? DBNull.Value);
            command.Parameters.AddWithValue("tipo_tarima", (object?)detalle.TipoTarima ?? DBNull.Value);
            command.Parameters.AddWithValue("consecutivo_tarima", detalle.ConsecutivoTarima);
            command.Parameters.AddWithValue("activo", detalle.Activo);
            command.Parameters.AddWithValue("creado_por", detalle.CreadoPor);
            command.Parameters.AddWithValue("modificado_por", (object?)detalle.ModificadoPor ?? DBNull.Value);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            insertado = LeerRegistro(reader);
        }

        await _stagingService.ActualizarTotalesAsync(detalle.ReciboId, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return insertado;
    }

    public async Task<IReadOnlyList<ReciboStagingDetalleModel>> ObtenerTarimasPorReciboAsync(
        long reciboId, bool soloActivas = true, CancellationToken cancellationToken = default)
    {
        var sql = $"""
            SELECT id, recibo_id, item_number, descripcion, lote, atributo, cantidad_piezas, cantidad_cajas, es_pnc, hu_id, tipo_tarima, consecutivo_tarima, activo, creado_por, modificado_por, creado_en, actualizado_en
            FROM recibos_staging_detalle
            WHERE recibo_id = @recibo_id{(soloActivas ? " AND activo = true" : string.Empty)}
            ORDER BY consecutivo_tarima, id;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("recibo_id", reciboId);

        var resultado = new List<ReciboStagingDetalleModel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            resultado.Add(LeerRegistro(reader));
        }

        return resultado;
    }

    /// <summary>
    /// Actualiza una tarima y registra en recibos_staging_cambios_log cada campo que
    /// realmente cambió (comparación campo por campo contra el valor leído dentro de la
    /// misma transacción, con FOR UPDATE), luego recalcula los totales de la cabecera.
    /// </summary>
    public async Task<ReciboStagingDetalleModel> ActualizarTarimaConLogAsync(
        long detalleId, ReciboStagingDetalleModel nuevoDetalle, string motivo, string usuario,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        ReciboStagingDetalleModel actual;
        await using (var comandoActual = new NpgsqlCommand(
            """
            SELECT id, recibo_id, item_number, descripcion, lote, atributo, cantidad_piezas, cantidad_cajas, es_pnc, hu_id, tipo_tarima, consecutivo_tarima, activo, creado_por, modificado_por, creado_en, actualizado_en
            FROM recibos_staging_detalle
            WHERE id = @id
            FOR UPDATE;
            """, connection, transaction))
        {
            comandoActual.Parameters.AddWithValue("id", detalleId);
            await using var reader = await comandoActual.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException($"No existe la tarima con id {detalleId} en recibos_staging_detalle.");
            }

            actual = LeerRegistro(reader);
        }

        ReciboStagingDetalleModel actualizado;
        await using (var comandoUpdate = new NpgsqlCommand(
            """
            UPDATE recibos_staging_detalle
            SET item_number = @item_number, descripcion = @descripcion, lote = @lote, atributo = @atributo,
                cantidad_piezas = @cantidad_piezas, cantidad_cajas = @cantidad_cajas, es_pnc = @es_pnc,
                hu_id = @hu_id, tipo_tarima = @tipo_tarima, consecutivo_tarima = @consecutivo_tarima,
                modificado_por = @modificado_por, actualizado_en = now()
            WHERE id = @id
            RETURNING id, recibo_id, item_number, descripcion, lote, atributo, cantidad_piezas, cantidad_cajas, es_pnc, hu_id, tipo_tarima, consecutivo_tarima, activo, creado_por, modificado_por, creado_en, actualizado_en;
            """, connection, transaction))
        {
            comandoUpdate.Parameters.AddWithValue("item_number", nuevoDetalle.ItemNumber);
            comandoUpdate.Parameters.AddWithValue("descripcion", (object?)nuevoDetalle.Descripcion ?? DBNull.Value);
            comandoUpdate.Parameters.AddWithValue("lote", (object?)nuevoDetalle.Lote ?? DBNull.Value);
            comandoUpdate.Parameters.AddWithValue("atributo", (object?)nuevoDetalle.Atributo ?? DBNull.Value);
            comandoUpdate.Parameters.AddWithValue("cantidad_piezas", nuevoDetalle.CantidadPiezas);
            comandoUpdate.Parameters.AddWithValue("cantidad_cajas", (object?)nuevoDetalle.CantidadCajas ?? DBNull.Value);
            comandoUpdate.Parameters.AddWithValue("es_pnc", nuevoDetalle.EsPnc);
            comandoUpdate.Parameters.AddWithValue("hu_id", (object?)nuevoDetalle.HuId ?? DBNull.Value);
            comandoUpdate.Parameters.AddWithValue("tipo_tarima", (object?)nuevoDetalle.TipoTarima ?? DBNull.Value);
            comandoUpdate.Parameters.AddWithValue("consecutivo_tarima", nuevoDetalle.ConsecutivoTarima);
            comandoUpdate.Parameters.AddWithValue("modificado_por", usuario);
            comandoUpdate.Parameters.AddWithValue("id", detalleId);

            await using var reader = await comandoUpdate.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            actualizado = LeerRegistro(reader);
        }

        foreach (var (campo, anterior, nuevo) in CompararCampos(actual, nuevoDetalle))
        {
            await using var comandoLog = new NpgsqlCommand(
                """
                INSERT INTO recibos_staging_cambios_log (recibo_id, detalle_id, entidad_modificada, campo_modificado, valor_anterior, valor_nuevo, motivo_cambio, usuario, fecha_cambio)
                VALUES (@recibo_id, @detalle_id, 'PALLET_DETALLE', @campo, @anterior, @nuevo, @motivo, @usuario, now());
                """, connection, transaction);
            comandoLog.Parameters.AddWithValue("recibo_id", actual.ReciboId);
            comandoLog.Parameters.AddWithValue("detalle_id", detalleId);
            comandoLog.Parameters.AddWithValue("campo", campo);
            comandoLog.Parameters.AddWithValue("anterior", (object?)anterior ?? DBNull.Value);
            comandoLog.Parameters.AddWithValue("nuevo", (object?)nuevo ?? DBNull.Value);
            comandoLog.Parameters.AddWithValue("motivo", (object?)motivo ?? DBNull.Value);
            comandoLog.Parameters.AddWithValue("usuario", usuario);
            await comandoLog.ExecuteNonQueryAsync(cancellationToken);
        }

        await _stagingService.ActualizarTotalesAsync(actual.ReciboId, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return actualizado;
    }

    /// <summary>
    /// Baja lógica (activo=false) de una tarima, con su propio registro de auditoría en
    /// recibos_staging_cambios_log, y recálculo de totales de la cabecera. No hace nada si
    /// la tarima ya estaba inactiva (evita una entrada de auditoría sin cambio real).
    /// </summary>
    public async Task EliminarTarimaLogicaAsync(
        long detalleId, string motivo, string usuario, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long reciboId;
        await using (var comandoActual = new NpgsqlCommand(
            "SELECT recibo_id, activo FROM recibos_staging_detalle WHERE id = @id FOR UPDATE;", connection, transaction))
        {
            comandoActual.Parameters.AddWithValue("id", detalleId);
            await using var reader = await comandoActual.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException($"No existe la tarima con id {detalleId} en recibos_staging_detalle.");
            }

            reciboId = reader.GetInt64(0);
            var activoActual = reader.GetBoolean(1);
            if (!activoActual)
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }
        }

        await using (var comandoUpdate = new NpgsqlCommand(
            "UPDATE recibos_staging_detalle SET activo = false, modificado_por = @usuario, actualizado_en = now() WHERE id = @id;", connection, transaction))
        {
            comandoUpdate.Parameters.AddWithValue("usuario", usuario);
            comandoUpdate.Parameters.AddWithValue("id", detalleId);
            await comandoUpdate.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var comandoLog = new NpgsqlCommand(
            """
            INSERT INTO recibos_staging_cambios_log (recibo_id, detalle_id, entidad_modificada, campo_modificado, valor_anterior, valor_nuevo, motivo_cambio, usuario, fecha_cambio)
            VALUES (@recibo_id, @detalle_id, 'PALLET_DETALLE', 'activo', 'true', 'false', @motivo, @usuario, now());
            """, connection, transaction))
        {
            comandoLog.Parameters.AddWithValue("recibo_id", reciboId);
            comandoLog.Parameters.AddWithValue("detalle_id", detalleId);
            comandoLog.Parameters.AddWithValue("motivo", (object?)motivo ?? DBNull.Value);
            comandoLog.Parameters.AddWithValue("usuario", usuario);
            await comandoLog.ExecuteNonQueryAsync(cancellationToken);
        }

        await _stagingService.ActualizarTotalesAsync(reciboId, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static IEnumerable<(string Campo, string? Anterior, string? Nuevo)> CompararCampos(
        ReciboStagingDetalleModel anterior, ReciboStagingDetalleModel nuevo)
    {
        if (anterior.ItemNumber != nuevo.ItemNumber)
            yield return ("item_number", anterior.ItemNumber, nuevo.ItemNumber);
        if (anterior.Descripcion != nuevo.Descripcion)
            yield return ("descripcion", anterior.Descripcion, nuevo.Descripcion);
        if (anterior.Lote != nuevo.Lote)
            yield return ("lote", anterior.Lote, nuevo.Lote);
        if (anterior.Atributo != nuevo.Atributo)
            yield return ("atributo", anterior.Atributo, nuevo.Atributo);
        if (anterior.CantidadPiezas != nuevo.CantidadPiezas)
            yield return ("cantidad_piezas", anterior.CantidadPiezas.ToString(), nuevo.CantidadPiezas.ToString());
        if (anterior.CantidadCajas != nuevo.CantidadCajas)
            yield return ("cantidad_cajas", anterior.CantidadCajas?.ToString(), nuevo.CantidadCajas?.ToString());
        if (anterior.EsPnc != nuevo.EsPnc)
            yield return ("es_pnc", anterior.EsPnc.ToString(), nuevo.EsPnc.ToString());
        if (anterior.HuId != nuevo.HuId)
            yield return ("hu_id", anterior.HuId, nuevo.HuId);
        if (anterior.TipoTarima != nuevo.TipoTarima)
            yield return ("tipo_tarima", anterior.TipoTarima, nuevo.TipoTarima);
        if (anterior.ConsecutivoTarima != nuevo.ConsecutivoTarima)
            yield return ("consecutivo_tarima", anterior.ConsecutivoTarima.ToString(), nuevo.ConsecutivoTarima.ToString());
    }

    private static ReciboStagingDetalleModel LeerRegistro(NpgsqlDataReader reader) => new(
        Id: reader.GetInt64(reader.GetOrdinal("id")),
        ReciboId: reader.GetInt64(reader.GetOrdinal("recibo_id")),
        ItemNumber: reader.GetString(reader.GetOrdinal("item_number")),
        Descripcion: reader.GetStringOrNull("descripcion"),
        Lote: reader.GetStringOrNull("lote"),
        Atributo: reader.GetStringOrNull("atributo"),
        CantidadPiezas: reader.GetInt32(reader.GetOrdinal("cantidad_piezas")),
        CantidadCajas: reader.GetInt32OrNull("cantidad_cajas"),
        EsPnc: reader.GetBoolean(reader.GetOrdinal("es_pnc")),
        HuId: reader.GetStringOrNull("hu_id"),
        TipoTarima: reader.GetStringOrNull("tipo_tarima"),
        ConsecutivoTarima: reader.GetInt32(reader.GetOrdinal("consecutivo_tarima")),
        Activo: reader.GetBoolean(reader.GetOrdinal("activo")),
        CreadoPor: reader.GetString(reader.GetOrdinal("creado_por")),
        ModificadoPor: reader.GetStringOrNull("modificado_por"),
        CreadoEn: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("creado_en")),
        ActualizadoEn: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("actualizado_en")));
}
