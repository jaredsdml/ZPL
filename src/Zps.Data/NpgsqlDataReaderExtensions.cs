using Npgsql;

namespace Zps.Data;

/// <summary>
/// Lectura de columnas nullable por nombre desde un NpgsqlDataReader. Evita repetir
/// IsDBNull/GetOrdinal en los servicios de recibos_staging, cuyos records (checklist de
/// calidad, transporte/tiempos) tienen decenas de columnas nullable.
/// </summary>
internal static class NpgsqlDataReaderExtensions
{
    public static string? GetStringOrNull(this NpgsqlDataReader reader, string columna)
    {
        var ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static bool? GetBooleanOrNull(this NpgsqlDataReader reader, string columna)
    {
        var ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
    }

    public static int? GetInt32OrNull(this NpgsqlDataReader reader, string columna)
    {
        var ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    public static DateTimeOffset? GetDateTimeOffsetOrNull(this NpgsqlDataReader reader, string columna)
    {
        var ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }
}
