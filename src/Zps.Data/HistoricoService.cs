using Npgsql;
using NpgsqlTypes;
using Zps.Data.Models;

namespace Zps.Data;

/// <summary>
/// Persistencia en Neon de los folios efectivamente impresos, en lote y en una sola
/// transacción/round-trip (NpgsqlBatch). Sustituye a GestorLPN.insertar_lpn_manual +
/// GestorLPN.guardar_respaldo_reimpresion (fila por fila, dos bases SQLite distintas)
/// del original por una única tabla (historico_impresiones) en Neon.
/// </summary>
public sealed class HistoricoService
{
    private readonly NpgsqlDataSource _dataSource;

    public HistoricoService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task InsertarLoteAsync(
        IReadOnlyCollection<HistoricoImpresionRecord> registros,
        CancellationToken cancellationToken = default)
    {
        if (registros.Count == 0)
        {
            return;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var batch = new NpgsqlBatch(connection, transaction);

        foreach (var r in registros)
        {
            var comando = new NpgsqlBatchCommand(
                """
                INSERT INTO historico_impresiones (lpn, consecutivo, tipo, cliente, solicitante, arribo, fecha_hora, sku, lote, cantidad, cajas, tarima_actual, tarima_total, variables_json)
                VALUES (@lpn, @consecutivo, @tipo, @cliente, @solicitante, @arribo, @fecha_hora, @sku, @lote, @cantidad, @cajas, @tarima_actual, @tarima_total, @variables_json)
                ON CONFLICT (lpn) DO NOTHING;
                """);
            comando.Parameters.AddWithValue("lpn", r.Lpn);
            comando.Parameters.AddWithValue("consecutivo", (object?)r.Consecutivo ?? DBNull.Value);
            comando.Parameters.AddWithValue("tipo", (object?)r.Tipo ?? DBNull.Value);
            comando.Parameters.AddWithValue("cliente", (object?)r.Cliente ?? DBNull.Value);
            comando.Parameters.AddWithValue("solicitante", r.Solicitante);
            comando.Parameters.AddWithValue("arribo", r.Arribo);
            // fecha_hora es timestamptz en Neon: Npgsql rechaza DateTimeOffset con offset
            // local distinto de cero, así que se normaliza a UTC puro aquí como resguardo,
            // sin importar en qué zona horaria haya sido construido el registro.
            comando.Parameters.AddWithValue("fecha_hora", r.FechaHora.ToUniversalTime());
            comando.Parameters.AddWithValue("sku", (object?)r.Sku ?? DBNull.Value);
            comando.Parameters.AddWithValue("lote", (object?)r.Lote ?? DBNull.Value);
            comando.Parameters.AddWithValue("cantidad", (object?)r.Cantidad ?? DBNull.Value);
            comando.Parameters.AddWithValue("cajas", (object?)r.Cajas ?? DBNull.Value);
            comando.Parameters.AddWithValue("tarima_actual", (object?)r.TarimaActual ?? DBNull.Value);
            comando.Parameters.AddWithValue("tarima_total", (object?)r.TarimaTotal ?? DBNull.Value);
            comando.Parameters.Add(new NpgsqlParameter("variables_json", NpgsqlDbType.Jsonb)
            {
                Value = (object?)r.VariablesJson ?? DBNull.Value
            });
            batch.BatchCommands.Add(comando);
        }

        await batch.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Busca folios impresos por coincidencia parcial (ILIKE) en LPN, cliente, arribo,
    /// solicitante, tipo, consecutivo (convertido a texto), SKU, lote y cajas — para el
    /// buscador rápido de la pestaña de Histórico y Reimpresiones. Sin filtro, devuelve los
    /// más recientes hasta <paramref name="limite"/>.
    /// </summary>
    public async Task<IReadOnlyList<HistoricoImpresionRecord>> BuscarAsync(
        string? filtro,
        int limite = 500,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT lpn, consecutivo, tipo, cliente, solicitante, arribo, fecha_hora, sku, lote, cantidad, cajas, tarima_actual, tarima_total, variables_json::text
            FROM historico_impresiones
            WHERE @filtro = ''
               OR lpn ILIKE '%' || @filtro || '%'
               OR cliente ILIKE '%' || @filtro || '%'
               OR arribo ILIKE '%' || @filtro || '%'
               OR solicitante ILIKE '%' || @filtro || '%'
               OR tipo ILIKE '%' || @filtro || '%'
               OR consecutivo::text ILIKE '%' || @filtro || '%'
               OR sku ILIKE '%' || @filtro || '%'
               OR lote ILIKE '%' || @filtro || '%'
               OR cajas ILIKE '%' || @filtro || '%'
            ORDER BY fecha_hora DESC
            LIMIT @limite;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("filtro", filtro ?? string.Empty);
        command.Parameters.AddWithValue("limite", limite);

        var resultado = new List<HistoricoImpresionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            resultado.Add(new HistoricoImpresionRecord(
                Lpn: reader.GetString(0),
                Consecutivo: reader.IsDBNull(1) ? null : reader.GetInt32(1),
                Tipo: reader.IsDBNull(2) ? null : reader.GetString(2),
                Cliente: reader.IsDBNull(3) ? null : reader.GetString(3),
                Solicitante: reader.GetString(4),
                Arribo: reader.GetString(5),
                FechaHora: reader.GetFieldValue<DateTimeOffset>(6),
                Sku: reader.IsDBNull(7) ? null : reader.GetString(7),
                Lote: reader.IsDBNull(8) ? null : reader.GetString(8),
                Cantidad: reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                Cajas: reader.IsDBNull(10) ? null : reader.GetString(10),
                TarimaActual: reader.IsDBNull(11) ? null : reader.GetInt32(11),
                TarimaTotal: reader.IsDBNull(12) ? null : reader.GetInt32(12),
                VariablesJson: reader.IsDBNull(13) ? null : reader.GetString(13)));
        }

        return resultado;
    }

    /// <summary>
    /// Aplica una edición de campos operativos (Sku/Lote/Cantidad) a un folio ya impreso y
    /// dentro de la misma transacción registra cada cambio en historico_cambios_log, para
    /// la acción "Editar y Reimprimir" de la pestaña Histórico. No toca consecutivo, cajas,
    /// tarima_actual/tarima_total ni variables_json: la reimpresión posterior sigue usando
    /// el TarimaActual/TarimaTotal originales para no alterar el "X de N" ya impreso.
    /// </summary>
    public async Task ActualizarConAuditoriaAsync(
        HistoricoImpresionRecord actualizado,
        IReadOnlyList<CambioCampo> cambios,
        string usuario,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long historicoId;
        await using (var comandoUpdate = new NpgsqlCommand(
            """
            UPDATE historico_impresiones
            SET sku = @sku, lote = @lote, cantidad = @cantidad, cajas = @cajas
            WHERE lpn = @lpn
            RETURNING id;
            """, connection, transaction))
        {
            comandoUpdate.Parameters.AddWithValue("sku", (object?)actualizado.Sku ?? DBNull.Value);
            comandoUpdate.Parameters.AddWithValue("lote", (object?)actualizado.Lote ?? DBNull.Value);
            comandoUpdate.Parameters.AddWithValue("cantidad", (object?)actualizado.Cantidad ?? DBNull.Value);
            comandoUpdate.Parameters.AddWithValue("cajas", (object?)actualizado.Cajas ?? DBNull.Value);
            comandoUpdate.Parameters.AddWithValue("lpn", actualizado.Lpn);
            var resultado = await comandoUpdate.ExecuteScalarAsync(cancellationToken);
            if (resultado is null)
            {
                throw new InvalidOperationException($"No existe el folio '{actualizado.Lpn}' en historico_impresiones.");
            }

            historicoId = (long)resultado;
        }

        foreach (var cambio in cambios)
        {
            await using var comandoLog = new NpgsqlCommand(
                """
                INSERT INTO historico_cambios_log (historico_id, lpn, campo_modificado, valor_anterior, valor_nuevo, usuario, fecha_cambio)
                VALUES (@historico_id, @lpn, @campo, @anterior, @nuevo, @usuario, @fecha);
                """, connection, transaction);
            comandoLog.Parameters.AddWithValue("historico_id", historicoId);
            comandoLog.Parameters.AddWithValue("lpn", actualizado.Lpn);
            comandoLog.Parameters.AddWithValue("campo", cambio.Campo);
            comandoLog.Parameters.AddWithValue("anterior", (object?)cambio.ValorAnterior ?? DBNull.Value);
            comandoLog.Parameters.AddWithValue("nuevo", (object?)cambio.ValorNuevo ?? DBNull.Value);
            comandoLog.Parameters.AddWithValue("usuario", usuario);
            comandoLog.Parameters.AddWithValue("fecha", DateTimeOffset.UtcNow);
            await comandoLog.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Corrige tarima_actual/tarima_total de un folio ya guardado, sin tocar ningún otro
    /// campo ni registrar auditoría: es la regla crítica del botón "Imprimir" del Generador
    /// Principal — si el operador partió el lote (recalculó el "X de N" respecto a lo ya
    /// guardado), la base de datos debe reflejar exactamente cómo salió la etiqueta física.
    /// </summary>
    public async Task ActualizarTarimaAsync(
        string lpn, int tarimaActual, int tarimaTotal, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE historico_impresiones SET tarima_actual = @tarima_actual, tarima_total = @tarima_total WHERE lpn = @lpn;",
            connection);
        command.Parameters.AddWithValue("tarima_actual", tarimaActual);
        command.Parameters.AddWithValue("tarima_total", tarimaTotal);
        command.Parameters.AddWithValue("lpn", lpn);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Elimina permanentemente los folios indicados de historico_impresiones (borrado
    /// seguro desde la pestaña de Histórico, protegido por contraseña en la UI). Devuelve
    /// la cantidad de filas realmente borradas.
    /// </summary>
    public async Task<int> EliminarAsync(IReadOnlyCollection<string> lpns, CancellationToken cancellationToken = default)
    {
        if (lpns.Count == 0)
        {
            return 0;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("DELETE FROM historico_impresiones WHERE lpn = ANY(@lpns);", connection);
        command.Parameters.AddWithValue("lpns", lpns.ToArray());
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
