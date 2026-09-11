namespace Zps.Updater;

/// <summary>
/// Resultado de consultar GitHub Releases. Un fallo (sin internet, timeout, respuesta
/// vacía/inválida) nunca se propaga como excepción hacia el llamador: siempre se traduce
/// en Exito=false con un mensaje, para que el arranque de la app nunca se vea bloqueado.
/// </summary>
public sealed record ReleaseCheckResult(bool Exito, ReleaseInfo? Release, string? Error)
{
    public static ReleaseCheckResult Ok(ReleaseInfo release) => new(true, release, null);

    public static ReleaseCheckResult Fallo(string error) => new(false, null, error);
}
