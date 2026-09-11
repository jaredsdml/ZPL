namespace Zps.Updater;

/// <summary>
/// Plantilla del script auxiliar (.bat) que hace el reemplazo atómico: espera a que el
/// proceso principal (Zps.UI.exe) termine, copia los archivos ya extraídos y validados
/// sobre la carpeta de instalación, y relanza la app. Nunca toca
/// %LOCALAPPDATA%\LogAm\ZPS\ (la caché SQLite): opera exclusivamente sobre la carpeta de
/// instalación que se le pasa como parámetro, que es un árbol de directorios totalmente
/// distinto.
/// </summary>
internal static class UpdaterScriptTemplate
{
    private const string Plantilla = """
        @echo off
        rem Zps.Updater - script auxiliar de reemplazo atomico (generado automaticamente).
        rem No editar a mano: se regenera en cada actualizacion.
        setlocal

        set "PID={{PID}}"
        set "INSTALL_DIR={{INSTALL_DIR}}"
        set "STAGED_DIR={{STAGED_DIR}}"
        set "EXE_NAME={{EXE_NAME}}"

        :esperar_cierre
        tasklist /FI "PID eq %PID%" 2>NUL | find /I "%PID%" >NUL
        if not errorlevel 1 (
            timeout /t 1 /nobreak >NUL
            goto esperar_cierre
        )

        rem Copia solo sobre la carpeta de instalacion; jamas toca %LOCALAPPDATA%\LogAm\ZPS\.
        robocopy "%STAGED_DIR%" "%INSTALL_DIR%" /E /IS /IT /R:3 /W:1 >NUL

        start "" "%INSTALL_DIR%\%EXE_NAME%"

        endlocal
        """;

    public static string Generar(int pidProcesoPrincipal, string carpetaInstalacion, string carpetaStaging, string nombreEjecutable)
    {
        return Plantilla
            .Replace("{{PID}}", pidProcesoPrincipal.ToString(), StringComparison.Ordinal)
            .Replace("{{INSTALL_DIR}}", carpetaInstalacion, StringComparison.Ordinal)
            .Replace("{{STAGED_DIR}}", carpetaStaging, StringComparison.Ordinal)
            .Replace("{{EXE_NAME}}", nombreEjecutable, StringComparison.Ordinal);
    }
}
