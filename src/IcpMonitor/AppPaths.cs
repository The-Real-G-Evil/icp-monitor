namespace IcpMonitor;

/// <summary>
/// Ablageorte. Einstellungen, Log und Sensorliste liegen im Benutzerprofil, weil die App
/// installiert unter "Programme" liegt und dort nicht schreiben darf.
/// </summary>
internal static class AppPaths
{
    public static string DataDir { get; } = CreateDataDir();

    public static string ConfigFile => Path.Combine(DataDir, "config.json");

    public static string LogFile => Path.Combine(DataDir, "icp-monitor.log");

    public static string SensorDumpFile => Path.Combine(DataDir, "sensors.txt");

    /// <summary>Mitgelieferte Standardkonfiguration neben der exe.</summary>
    public static string DefaultConfigFile => Path.Combine(AppContext.BaseDirectory, "config.default.json");

    /// <summary>Konfiguration aelterer Versionen, die noch neben der exe lag.</summary>
    public static string LegacyConfigFile => Path.Combine(AppContext.BaseDirectory, "config.json");

    private static string CreateDataDir()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ICP Monitor");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
