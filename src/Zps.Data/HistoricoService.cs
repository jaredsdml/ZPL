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
                INSERT INTO historico_impresiones (lpn, consecutivo, tipo, cliente, solicitante, arribo, fecha_hora, sku, lote, cantidad, cajas, variables_json)
                VALUES (@lpn, @consecutivo, @tipo, @cliente, @solicitante, @arribo, @fecha_hora, @sku, @lote, @cantidad, @cajas, @variables_json)
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
            SELECT lpn, consecutivo, tipo, cliente, solicitante, arribo, fecha_hora, sku, lote, cantidad, cajas, variables_json::text
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
                VariablesJson: reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        return resultado;
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
