using System.Text.Encodings.Web;
using System.Text.Json;

namespace IcpMonitor;

internal sealed class AppConfig
{
    /// <summary>Die sechs Seitentasten des ICP in der Reihenfolge, wie sie auf dem Geraet liegen.</summary>
    public static readonly string[] SlotButtons = ["COM1", "COM2", "IFF", "LIST", "A-A", "A-G"];

    /// <summary>"auto" (Sprache des Betriebssystems), "de" oder "en".</summary>
    public string Language { get; set; } = L.Auto;

    public int RefreshMs { get; set; } = 500;

    public int SensorIntervalMs { get; set; } = 1000;

    /// <summary>Fehlersuche: jeden geaenderten Displayinhalt ins Log schreiben.</summary>
    public bool LogDisplay { get; set; }

    /// <summary>Taste, deren Seite beim Start angezeigt wird (z. B. "COM1").</summary>
    public string StartSlot { get; set; } = "COM1";

    /// <summary>Welche Seite auf welcher Taste liegt.</summary>
    public List<SlotConfig> Slots { get; set; } = [];

    /// <summary>Aktionen je Zifferntaste ("0" bis "9").</summary>
    public Dictionary<string, ActionConfig> Actions { get; set; } = new();

    /// <summary>Hardware-Lichtkanaele, die von den Potis SYM, BRT und CONT gesteuert werden.</summary>
    public LightConfig Lights { get; set; } = new();

    public static string FilePath => AppPaths.ConfigFile;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Slot zu einer Taste; fehlt er in der Datei, wird er angelegt.</summary>
    public SlotConfig Slot(string button)
    {
        SlotConfig? slot = Slots.Find(s => s.Button.Equals(button, StringComparison.OrdinalIgnoreCase));
        if (slot == null)
        {
            slot = new SlotConfig { Button = button };
            Slots.Add(slot);
        }
        return slot;
    }

    public AppConfig Clone() => JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    public static AppConfig Load()
    {
        AppConfig config;
        try
        {
            string? source = FirstExisting(FilePath, AppPaths.LegacyConfigFile, AppPaths.DefaultConfigFile);
            if (source != null)
            {
                config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(source), JsonOptions) ?? new AppConfig();
                if (source != FilePath) Log.Info($"Konfiguration uebernommen aus {source}");
            }
            else
            {
                Log.Warn("Keine config.json gefunden, verwende Standardwerte.");
                config = new AppConfig();
            }
        }
        catch (Exception ex)
        {
            Log.Error("config.json konnte nicht gelesen werden, verwende Standardwerte", ex);
            config = new AppConfig();
        }
        config.Normalize();
        if (!File.Exists(FilePath))
        {
            // Erster Start: Datei im Benutzerprofil anlegen, damit sie per Tray-Menue geoeffnet werden kann.
            try { config.Save(); }
            catch (Exception ex) { Log.Warn($"config.json konnte nicht angelegt werden: {ex.Message}"); }
        }
        return config;
    }

    private static string? FirstExisting(params string[] paths) => paths.FirstOrDefault(File.Exists);

    public void Save()
    {
        Normalize();
        string tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tempPath, FilePath, overwrite: true);
        Log.Info("Konfiguration gespeichert.");
    }

    private void Normalize()
    {
        RefreshMs = Math.Clamp(RefreshMs, 100, 5000);
        SensorIntervalMs = Math.Clamp(SensorIntervalMs, 250, 10000);
        Actions = new Dictionary<string, ActionConfig>(Actions, StringComparer.OrdinalIgnoreCase);

        bool firstStart = Slots.Count == 0;
        // Genau ein Slot je Taste, in Geraete-Reihenfolge.
        Slots = SlotButtons.Select(button => Slots.Find(s => s.Button.Equals(button, StringComparison.OrdinalIgnoreCase))
                                             ?? new SlotConfig { Button = button }).ToList();
        if (firstStart) ApplyDefaultSlots();
        foreach (SlotConfig slot in Slots)
        {
            if (slot.Rss == null) continue;
            slot.Rss.Feeds = slot.Rss.Feeds.Select(f => f.Trim()).Where(f => f.Length > 0).Distinct().ToList();
            slot.Rss.RotateSeconds = Math.Clamp(slot.Rss.RotateSeconds, 3, 300);
            // Leere RSS-Einstellungen bei anderen Seiten nicht mitspeichern; eingetragene Feeds aber behalten.
            if (!slot.Page.Equals("RSS", StringComparison.OrdinalIgnoreCase) && slot.Rss.Feeds.Count == 0) slot.Rss = null;
        }
    }

    private void ApplyDefaultSlots()
    {
        string[] pages = ["SYSTEM", "CPU", "GPU", "MEMORY", "RSS", "STORAGE"];
        for (int i = 0; i < Slots.Count; i++) Slots[i].Page = pages[i];
        Slots[4].Rss = new RssOptions
        {
            Feeds = ["https://www.tagesschau.de/index~rss2.xml", "https://www.heise.de/rss/heise-top-atom.xml"],
        };
    }
}

internal sealed class SlotConfig
{
    public string Button { get; set; } = "";

    /// <summary>Seitentyp aus dem <see cref="Pages.PageCatalog"/>, "NONE" = nicht belegt.</summary>
    public string Page { get; set; } = "NONE";

    /// <summary>Nur fuer RSS-Seiten.</summary>
    public RssOptions? Rss { get; set; }
}

internal sealed class RssOptions
{
    public List<string> Feeds { get; set; } = [];

    /// <summary>Nach wie vielen Sekunden automatisch die naechste Meldung kommt.</summary>
    public int RotateSeconds { get; set; } = 10;
}

/// <summary>
/// Kanalnummern fuer das Winwing-Lichtkommando. Die Belegung beim ICP ist nicht dokumentiert;
/// Kanal 0 (Tasten-LEDs) und 1 (DED) sind am Geraet bestaetigt. Mit der ICP-Testseite lassen
/// sich weitere Kanaele ausprobieren. -1 schaltet die Steuerung ab.
/// </summary>
internal sealed class LightConfig
{
    /// <summary>Hintergrundbeleuchtung des DED, gesteuert vom Poti SYM.</summary>
    public int DisplayChannel { get; set; } = 1;

    /// <summary>Beleuchtung der Tasten (LEDs), gesteuert vom Poti BRT.</summary>
    public int KeysChannel { get; set; } = 0;

    /// <summary>
    /// Kontrast des DED, gesteuert vom Poti CONT. Am Geraet wurden die Kanaele 0-15 getestet,
    /// keiner aendert den Kontrast - daher aus. Wird ein Kanal gefunden, hier eintragen.
    /// </summary>
    public int ContrastChannel { get; set; } = -1;

    /// <summary>Untergrenze fuer das DED in Prozent, damit es nie ganz dunkel wird.</summary>
    public int DisplayMinPercent { get; set; } = 5;
}

internal sealed class ActionConfig
{
    public string Name { get; set; } = "";

    public string Command { get; set; } = "";

    public string Arguments { get; set; } = "";
}
