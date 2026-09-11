using Npgsql;
using Zps.Core;
using Zps.Core.Models;

namespace Zps.Data;

/// <summary>
/// Reserva atómica de consecutivos de folio LPN contra Neon.
///
/// Sustituye al patrón "SELECT MAX(consecutivo) ... cache en memoria" de
/// GestorLPN.generar_folios (app_centralizada.py) por un contador dedicado
/// (secuencias_lpn) bloqueado con SELECT ... FOR UPDATE, de modo que dos procesos
/// (o dos hilos) que reserven al mismo tiempo bajo la misma clave nunca reciban
/// consecutivos superpuestos, y el bloque reservado sea siempre contiguo (sin huecos).
/// </summary>
public sealed class ConsecutivosService
{
    private readonly NpgsqlDataSource _dataSource;

    public ConsecutivosService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <summary>
    /// Reserva un bloque contiguo de <paramref name="cantidad"/> consecutivos bajo
    /// <paramref name="clave"/> y devuelve los números reservados en orden ascendente.
    /// Si la fila de secuencia no existe todavía (p. ej. un año nuevo), se crea en 0
    /// dentro de la misma transacción antes de bloquearla.
    /// </summary>
    public async Task<int[]> ReservarLoteAsync(
        string clave,
        string tipo,
        int cantidad,
        int? anio = null,
        CancellationToken cancellationToken = default)
    {
        if (cantidad <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cantidad), cantidad, "La cantidad a reservar debe ser mayor a cero.");
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var upsert = new NpgsqlCommand(
            """
            INSERT INTO secuencias_lpn (clave, tipo, anio, ultimo_consecutivo)
            VALUES (@clave, @tipo, @anio, 0)
            ON CONFLICT (clave) DO NOTHING;
            """,
            connection,
            transaction))
        {
            upsert.Parameters.AddWithValue("clave", clave);
            upsert.Parameters.AddWithValue("tipo", tipo);
            upsert.Parameters.AddWithValue("anio", (object?)anio ?? DBNull.Value);
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        int ultimo;
        await using (var select = new NpgsqlCommand(
            "SELECT ultimo_consecutivo FROM secuencias_lpn WHERE clave = @clave FOR UPDATE;",
            connection,
            transaction))
        {
            select.Parameters.AddWithValue("clave", clave);
            var valor = await select.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException($"No se encontró la secuencia '{clave}' tras el upsert.");
            ultimo = (int)valor;
        }

        var nuevoUltimo = ultimo + cantidad;

        await using (var update = new NpgsqlCommand(
            "UPDATE secuencias_lpn SET ultimo_consecutivo = @nuevo, actualizado_en = now() WHERE clave = @clave;",
            connection,
            transaction))
        {
            update.Parameters.AddWithValue("nuevo", nuevoUltimo);
            update.Parameters.AddWithValue("clave", clave);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var reservados = new int[cantidad];
        for (var i = 0; i < cantidad; i++)
        {
            reservados[i] = ultimo + i + 1;
        }

        return reservados;
    }

    /// <summary>
    /// Sobrecarga de conveniencia que deriva la clave de secuencia a partir del tipo de
    /// dominio (Zps.Core.MinisoLpnEngine.ClaveSecuencia), para el flujo normal de la app.
    /// </summary>
    public Task<int[]> ReservarLoteAsync(
        TipoSecuenciaLpn tipo,
        int anio,
        int cantidad,
        CancellationToken cancellationToken = default)
    {
        var clave = MinisoLpnEngine.ClaveSecuencia(tipo, anio);
        var tipoTexto = tipo switch
        {
            TipoSecuenciaLpn.Normal => "NORMAL",
            TipoSecuenciaLpn.Pnc => "PNC",
            TipoSecuenciaLpn.MnsPncD => "MNS_PNC_D",
            TipoSecuenciaLpn.MnsPncM => "MNS_PNC_M",
            _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, null)
        };
        var anioParaFila = tipo is TipoSecuenciaLpn.Normal or TipoSecuenciaLpn.Pnc ? anio : (int?)null;

        return ReservarLoteAsync(clave, tipoTexto, cantidad, anioParaFila, cancellationToken);
    }
}
