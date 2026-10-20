namespace IcpMonitor.Games;

/// <param name="Processes">Prozessnamen ohne ".exe", wie sie <see cref="System.Diagnostics.Process.ProcessName"/> liefert.</param>
internal sealed record GameDefinition(string Id, string Name, string[] Processes, bool DefaultEnabled = true);

/// <summary>Programme, die das ICP selbst nutzen und deshalb Vorrang bekommen.</summary>
internal static class GameCatalog
{
    public static readonly GameDefinition[] BuiltIn =
    [
        new("DCS", "DCS World", ["DCS"]),
        new("MSFS", "Microsoft Flight Simulator", ["FlightSimulator", "FlightSimulator2024"]),
        new("BMS", "Falcon BMS", ["Falcon BMS"]),
        new("XPLANE", "X-Plane", ["X-Plane"]),
        // Laeuft oft dauerhaft im Hintergrund, daher standardmaessig aus.
        new("SIMAPPPRO", "SimAppPro", ["SimAppPro"], DefaultEnabled: false),
    ];

    /// <summary>"C:\...\Foo.exe" oder "Foo.exe" -> "Foo".</summary>
    public static string ProcessNameFromPath(string pathOrName)
    {
        string name = Path.GetFileName(pathOrName.Trim());
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    /// <summary>Alle Programme, die laut Konfiguration eine Pause ausloesen.</summary>
    public static List<GameDefinition> Watched(PauseConfig config)
    {
        var watched = BuiltIn.Where(config.IsEnabled).ToList();
        watched.AddRange(config.Custom
            .Where(c => c.Enabled && !string.IsNullOrWhiteSpace(c.Process))
            .Select(c => new GameDefinition("CUSTOM", string.IsNullOrWhiteSpace(c.Name) ? c.Process : c.Name, [ProcessNameFromPath(c.Process)])));
        return watched;
    }
}
