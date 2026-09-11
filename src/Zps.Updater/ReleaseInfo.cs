namespace Zps.Updater;

/// <summary>
/// Datos relevantes de la última release publicada en GitHub, ya interpretados
/// (tag parseado a Version, URL del asset .zip si existe, notas de la release).
/// </summary>
public sealed record ReleaseInfo(
    string TagName,
    Version Version,
    string? ZipAssetUrl,
    string? ZipAssetNombre,
    string? ChecksumAssetUrl,
    string? NotasVersion);
