using System.Globalization;

namespace IcpMonitor;

internal enum Language { De, En }

/// <summary>
/// Alle sichtbaren Texte in allen Sprachen. Die Sprache folgt dem Betriebssystem,
/// kann aber in den Einstellungen festgelegt werden ("language" in config.json).
/// Texte fuers DED stehen in Grossbuchstaben ohne Umlaute - die Schrift kennt nur ASCII.
/// Neue Sprache: Wert in <see cref="Language"/> ergaenzen und in <see cref="S"/> einen Parameter hinzufuegen.
/// </summary>
internal static class L
{
    public const string Auto = "auto";

    public static Language Current { get; private set; } = SystemLanguage;

    /// <summary>Kultur fuer Datum, Wochentag usw.: die des Systems, wenn die Sprache passt.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentCulture;

    public static Language SystemLanguage =>
        CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "de" ? Language.De : Language.En;

    /// <summary>Sprache aus der Konfiguration setzen: "auto", "de" oder "en".</summary>
    public static void SetLanguage(string? setting)
    {
        Current = Parse(setting) ?? SystemLanguage;
        string code = Current == Language.De ? "de" : "en";
        Culture = CultureInfo.CurrentCulture.TwoLetterISOLanguageName == code
            ? CultureInfo.CurrentCulture
            : CultureInfo.GetCultureInfo(Current == Language.De ? "de-DE" : "en-US");
    }

    public static Language? Parse(string? setting) => setting?.Trim().ToLowerInvariant() switch
    {
        "de" => Language.De,
        "en" => Language.En,
        _ => null,
    };

    public static string Code(Language language) => language == Language.De ? "de" : "en";

    public static string Name(Language language) => language == Language.De ? "Deutsch" : "English";

    private static string S(string de, string en) => Current == Language.De ? de : en;

    // ---------- Tray ----------
    public static string TraySettings => S("Einstellungen ...", "Settings ...");
    public static string TrayConnected => S("ICP: verbunden", "ICP: connected");
    public static string TrayDisconnected => S("ICP: nicht verbunden", "ICP: not connected");
    public static string TrayConnecting => S("ICP: verbinde ...", "ICP: connecting ...");
    public static string TrayShowPage => S("Seite anzeigen", "Show page");
    public static string TrayDisplayOn => S("Display an", "Display on");
    public static string TrayTestPage => S("ICP-Testseite anzeigen", "Show ICP test page");
    public static string TrayAutostart => S("Mit Windows starten", "Start with Windows");
    public static string TrayPause => S("Pausieren", "Pause");
    public static string AutostartOnlyInstalled => S(
        "Der Autostart ist nur für die installierte Version (unter „Programme“) möglich. Bitte ICP Monitor über den Installer installieren.",
        "Autostart is only available for the installed version (under \"Program Files\"). Please install ICP Monitor with the installer.");
    public static string TrayPausedTip(string? game) => game != null
        ? S($"Pausiert – {game} läuft", $"Paused – {game} is running")
        : S("Pausiert", "Paused");
    public static string TrayCheckUpdates => S("Nach Updates suchen", "Check for updates");
    public static string TrayInstallUpdate(Version v) => S($"Update auf {v} installieren ...", $"Install update {v} ...");

    // ---------- Updates ----------
    public static string UpdateTitle => "ICP Monitor – Update";
    public static string UpdateAvailableBalloon(Version v) => S($"Version {v} ist verfügbar. Hier klicken, um sie zu installieren.",
                                                                $"Version {v} is available. Click here to install it.");
    public static string UpdateConfirm(Version current, Version next, string notes) =>
        S($"ICP Monitor {next} ist verfügbar (installiert: {current}).\n\n{notes}\n\nJetzt herunterladen und installieren? ICP Monitor wird dabei kurz beendet und danach neu gestartet.",
          $"ICP Monitor {next} is available (installed: {current}).\n\n{notes}\n\nDownload and install now? ICP Monitor will close briefly and restart afterwards.");
    public static string UpdateUpToDate(Version v) => S($"ICP Monitor {v} ist aktuell.", $"ICP Monitor {v} is up to date.");
    public static string UpdateNoReleases => S("Auf GitHub wurde keine veröffentlichte Version gefunden.", "No published version was found on GitHub.");
    public static string UpdateFailed(string message) => S($"Die Update-Suche ist fehlgeschlagen:\n{message}", $"The update check failed:\n{message}");
    public static string UpdateDownloading(Version v) => S($"Lade ICP Monitor {v} herunter ...", $"Downloading ICP Monitor {v} ...");
    public static string UpdateInstallFailed(string message) => S($"Das Update konnte nicht installiert werden:\n{message}", $"The update could not be installed:\n{message}");
    public static string UpdateNoInstaller => S("Das Release enthält keinen Installer.", "The release contains no installer.");
    public static string UpdateNoChecksum => S("Das Release enthält keine Prüfsumme (.sha256) – aus Sicherheitsgründen wird nicht installiert.",
                                               "The release contains no checksum (.sha256) – not installing for safety reasons.");
    public static string UpdateChecksumMismatch => S("Die Prüfsumme des heruntergeladenen Installers stimmt nicht – er wird nicht ausgeführt.",
                                                     "The checksum of the downloaded installer does not match – it will not be run.");
    public static string AutostartFailed => S("Der Autostart konnte nicht geändert werden. Details stehen in der Logdatei.",
                                              "Autostart could not be changed. See the log file for details.");
    public static string TrayRestartAdmin => S("Als Administrator neu starten (mehr Sensoren)", "Restart as administrator (more sensors)");
    public static string TrayOpenSensors => S("Sensorliste öffnen", "Open sensor list");
    public static string TrayOpenConfig => S("Konfigurationsdatei öffnen", "Open configuration file");
    public static string TrayOpenLog => S("Logdatei öffnen", "Open log file");
    public static string TrayExit => S("Beenden", "Exit");
    public static string TrayNotConnectedTip => S("ICP nicht verbunden", "ICP not connected");
    public static string FileMissing(string path) => S($"Datei existiert (noch) nicht:\n{path}", $"File does not exist (yet):\n{path}");
    public static string AlreadyRunning => S("ICP Monitor läuft bereits (siehe Taskleiste).", "ICP Monitor is already running (see taskbar).");

    // ---------- Einstellungen ----------
    public static string SettingsTitle => S("ICP Monitor – Einstellungen", "ICP Monitor – Settings");
    public static string SettingsKey(string button) => S($"Taste {button}", $"Button {button}");
    public static string SettingsPage => S("Seite:", "Page:");
    public static string SettingsFeeds => S("Feeds (eine Adresse pro Zeile):", "Feeds (one address per line):");
    public static string SettingsRotate => S("Nächste Meldung nach (Sekunden):", "Next headline after (seconds):");
    public static string SettingsShowNow => S("Jetzt auf dem ICP anzeigen", "Show on the ICP now");
    public static string SettingsTestPage => S("ICP-Testseite ein/aus", "ICP test page on/off");
    public static string SettingsLanguage => S("Sprache:", "Language:");
    public static string SettingsLanguageAuto(Language system) => S($"Automatisch ({Name(system)})", $"Automatic ({Name(system)})");
    public static string Ok => "OK";
    public static string Apply => S("Übernehmen", "Apply");
    public static string Close => S("Schließen", "Close");
    public static string Cancel => S("Abbrechen", "Cancel");
    public static string SettingsGeneral => S("Allgemein", "General");
    public static string TabKeys => S("ICP-Tasten", "ICP buttons");
    public static string TabGames => S("Spiele", "Games");
    public static string GamesAutoPause => S("Automatisch pausieren, solange eines dieser Programme läuft",
                                             "Pause automatically while one of these programs is running");
    public static string GamesExplain => S("Während der Pause überlässt ICP Monitor das ICP komplett dem Programm: kein Display, keine Tasten, keine Beleuchtung.",
                                           "While paused, ICP Monitor leaves the ICP entirely to the program: no display, no buttons, no lighting.");
    public static string GamesAddExe => S("Programm hinzufügen ...", "Add program ...");
    public static string GamesAddRunning => S("Aus laufenden Programmen ...", "From running programs ...");
    public static string GamesRemove => S("Entfernen", "Remove");
    public static string GamesExeFilter => S("Programme (*.exe)|*.exe", "Programs (*.exe)|*.exe");
    public static string GamesPickTitle => S("Laufendes Programm auswählen", "Select running program");
    public static string GamesColumnProcess => S("Programm", "Program");
    public static string GamesColumnWindow => S("Fenster", "Window");
    public static string UpdatesGroup => "Updates";
    public static string UpdatesAuto => S("Beim Start und regelmäßig nach neuen Versionen suchen", "Check for new versions at startup and regularly");
    public static string UpdatesCheckNow => S("Jetzt nach Updates suchen", "Check for updates now");
    public static string InstalledVersion(Version v) => S($"Installierte Version: {v}", $"Installed version: {v}");
    public static string AskSaveChanges => S("Änderungen übernehmen?", "Apply changes?");
    public static string SaveFailed(string message) => S($"Die Einstellungen konnten nicht gespeichert werden:\n{message}", $"The settings could not be saved:\n{message}");

    // ---------- Seitentypen ----------
    public static string PageNone => S("(nicht belegt)", "(not assigned)");
    public static string PageNoneDesc => S("Die Taste zeigt keine Seite an.", "The button shows no page.");
    public static string PageSystem => "System";
    public static string PageSystemDesc => S("Die wichtigsten Werte auf einen Blick: Last, Temperatur, Leistung und Takt von CPU und GPU, dazu RAM und Grafikspeicher.",
                                             "The key values at a glance: load, temperature, power and clock of CPU and GPU, plus RAM and video memory.");
    public static string PageCpu => S("Prozessor", "Processor");
    public static string PageCpuDesc => S("Auslastung, Temperatur, Leistung und Takt der CPU.", "CPU load, temperature, power and clock.");
    public static string PageGpu => S("Grafikkarte", "Graphics card");
    public static string PageGpuDesc => S("Auslastung, Temperaturen, Leistung, Takt, Lüfter und Grafikspeicher.", "Load, temperatures, power, clock, fan and video memory.");
    public static string PageMemory => S("Speicher", "Memory");
    public static string PageMemoryDesc => S("Arbeitsspeicher, virtueller Speicher und Grafikspeicher.", "RAM, virtual memory and video memory.");
    public static string PageStorage => S("Laufwerke", "Drives");
    public static string PageStorageDesc => S("Belegung und freier Platz der Laufwerke, Temperatur und Aktivität der Datenträger.\nDobber hoch/runter: blättern, wenn nicht alles auf eine Seite passt.",
                                              "Usage and free space of the drives, temperature and activity of the disks.\nDobber up/down: scroll if everything does not fit on one screen.");
    public static string PageNetwork => S("Netzwerk", "Network");
    public static string PageNetworkDesc => S("Aktuelle Download-/Upload-Rate und übertragene Datenmenge.", "Current download/upload rate and amount of data transferred.");
    public static string PageClock => S("Uhr", "Clock");
    public static string PageClockDesc => S("Wochentag, Datum, Uhrzeit mit Sekunden und Laufzeit seit dem Systemstart.", "Weekday, date, time with seconds and uptime since system start.");
    public static string PageRss => S("RSS-Reader", "RSS reader");
    public static string PageRssDesc => S("Schlagzeilen aus RSS-/Atom-Feeds, wechseln automatisch.\nDobber hoch/runter: vorige/nächste Meldung. ENTR: Artikel im Browser öffnen.",
                                          "Headlines from RSS/Atom feeds, changing automatically.\nDobber up/down: previous/next headline. ENTR: open article in browser.");

    // ---------- DED (nur ASCII-Grossbuchstaben, max. Breite beachten) ----------
    public static string DedSystem => "SYSTEM";
    public static string DedMemory => S("SPEICHER", "MEMORY");
    public static string DedNetwork => S("NETZWERK", "NETWORK");
    public static string DedClock => S("UHR", "CLOCK");
    public static string DedDrives => S("LAUFWERKE", "DRIVES");
    /// <summary>4 Zeichen: Auslastung</summary>
    public static string DedLoad => S("LAST", "LOAD");
    /// <summary>4 Zeichen: Takt</summary>
    public static string DedClockSpeed => S("TAKT", "CLK ");
    /// <summary>5 Zeichen: Leistung</summary>
    public static string DedPower => S("LEIST", "POWER");
    /// <summary>5 Zeichen: Luefter</summary>
    public static string DedFan => S("LUEFT", "FAN  ");
    /// <summary>4 Zeichen: hoechste Kernlast</summary>
    public static string DedCore => S("KERN", "CORE");
    /// <summary>4 Zeichen: frei</summary>
    public static string DedFree => S("FREI", "FREE");
    public static string DedDisksHeader => S("DATENTRAEGER  TEMP AKTIV", "DISK          TEMP  BUSY");
    public static string DedNoDrives => S("KEINE LAUFWERKE", "NO DRIVES");
    public static string DedDown => "DOWN";
    public static string DedUp => "UP  ";
    public static string DedTotal => S("GESAMT", "TOTAL ");
    public static string DedUptime => S("LAUFZEIT", "UPTIME");
    /// <summary>Tage-Kuerzel der Laufzeit, z. B. "3T" / "3D".</summary>
    public static string DedDayShort => S("T", "D");
    public static string DedKeyNotAssigned(string key) => S($"{key}: NICHT BELEGT", $"{key}: NOT ASSIGNED");
    public static string DedKeyLabel(string key) => S($"TASTE {key}", $"BUTTON {key}");
    public static string DedNotAssigned => S("NICHT BELEGT", "NOT ASSIGNED");
    public static string DedNoAction(int digit) => S($"TASTE {digit}: KEINE AKTION", $"BUTTON {digit}: NO ACTION");
    public static string DedError => S("FEHLER", "ERROR");
    public static string DedLastInput => S("LETZTE: ", "LAST: ");
    public static string DedSwitches => S("SCHALTER", "SWITCH  ");
    public static string DedLightChannel(int channel, int value) => S($"LICHT K{channel,-2}  {value,3}%", $"LIGHT CH{channel,-2} {value,3}%");
    public static string DedNoFeeds => S("KEINE FEEDS EINGETRAGEN", "NO FEEDS CONFIGURED");
    public static string DedFeedsHint1 => S("TRAY-MENUE >", "TRAY MENU >");
    public static string DedFeedsHint2 => S("EINSTELLUNGEN", "SETTINGS");
    public static string DedLoadingFeeds => S("LADE FEEDS...", "LOADING FEEDS...");
    public static string DedMinutesAgo(int minutes) => S($"VOR {minutes} MIN", $"{minutes} MIN AGO");
    public static string DedHoursAgo(int hours) => S($"VOR {hours} STD", $"{hours} H AGO");
    public static string DedGameStarted(string game) => S($"{game} gestartet.", $"{game} started.");
    public static string DedMonitorPaused => S("ICP MONITOR WIRD PAUSIERT.", "ICP MONITOR IS PAUSED.");
}
