# ICP Monitor

Zeigt PC-Sensorwerte auf dem DED-Display eines Winwing/WinCTRL F-16 ICP an. Läuft im Hintergrund
mit Symbol im Infobereich der Taskleiste und wird über die Tasten und Potis des ICP bedient.

*English:* Shows PC sensor values (CPU, GPU, RAM, drives, network, clock, RSS headlines) on the DED
display of a Winwing/WinCTRL F-16 ICP. Runs in the system tray, is operated with the ICP's own buttons
and knobs, pauses automatically while a flight simulator uses the ICP and updates itself from GitHub.
The app speaks German and English. See [English summary](#english-summary) below.

![Einstellungsfenster mit ICP-Abbild und DED-Vorschau](docs/settings-buttons.png)

> ICP Monitor ist ein unabhängiges Hobbyprojekt und steht in keiner Verbindung zu Winwing.
> *Independent hobby project, not affiliated with or endorsed by Winwing.*

## Funktionen

- Sechs frei belegbare Seiten auf `COM1`, `COM2`, `IFF`, `LIST`, `A-A`, `A-G`: System, Prozessor,
  Grafikkarte, Speicher, Laufwerke, Netzwerk, Uhr, RSS-Reader
- Original-DED-Pixelschrift, Einstellungsfenster mit pixelgenauer Vorschau
- Display- und Tastenhelligkeit über die Potis `SYM` und `BRT`
- Automatische Pause, solange DCS World, MSFS, Falcon BMS, X-Plane oder eigene Programme laufen
- Autostart mit Administratorrechten ohne UAC-Abfrage, Auto-Update mit Prüfsumme
- Deutsch und Englisch

## Installation

Den Installer `ICP-Monitor-Setup-<Version>.exe` aus den
[Releases](https://github.com/The-Real-G-Evil/icp-monitor/releases/latest) herunterladen und ausführen.
Er bringt die .NET-Laufzeit mit, installiert nach `C:\Program Files\ICP Monitor`, legt einen
Startmenü-Eintrag an und richtet auf Wunsch den Autostart ein. Eine laufende Instanz wird beim Update
automatisch beendet.

Hinweis: Der Installer ist nicht digital signiert; Windows SmartScreen kann deshalb beim ersten Start
warnen („Weitere Informationen“ > „Trotzdem ausführen“).

**Autostart** läuft über die Windows-Aufgabenplanung (Aufgabe „ICP Monitor“): Start bei der Anmeldung
mit Administratorrechten, ohne UAC-Abfrage. Ein- und ausschalten im Tray-Menü unter „Mit Windows starten“.

Mit Administratorrechten liefert die App zusätzlich CPU-Temperatur, CPU-Leistung, Takt,
Mainboard-Lüfter und Datenträger-Temperaturen. Ohne Autostart: Tray-Menü > „Als Administrator neu starten“.

Einstellungen, Log und Sensorliste liegen in `%AppData%\ICP Monitor` und bleiben bei Updates und
Deinstallation erhalten.

## Bauen

Voraussetzung: .NET 10 SDK; für den Installer zusätzlich Inno Setup 6 (`winget install JRSoftware.InnoSetup`).

```
dotnet build IcpMonitor.slnx
.\build-installer.ps1
```

Startparameter: `--settings` öffnet direkt das Einstellungsfenster, `--autostart on|off` richtet
den Autostart ein bzw. entfernt ihn (braucht Administratorrechte).

## Bedienung

Tray-Symbol: Linksklick = nächste Seite, Doppelklick = Einstellungen, Rechtsklick = Menü
(u. a. „Mit Windows starten“).

| ICP | Funktion |
| --- | --- |
| `COM1` `COM2` `IFF` `LIST` `A-A` `A-G` | Seite dieser Taste anzeigen |
| Dobber hoch/runter | in der Seite blättern (RSS, Laufwerke), sonst vorige/nächste Seite |
| `ENTR` auf der RSS-Seite | Artikel im Browser öffnen |
| `0`–`9` | Aktion aus `config.json` (`actions`) |
| `WARN RESET` | Display aus; jede Taste schaltet es wieder ein |
| Poti `SYM` | Helligkeit des Displays |
| Poti `BRT` | Helligkeit der Tasten-LEDs |
| `UP` `DOWN` `SEQ` `RTN`, Potis `CONT` `RET DEPR` | ohne Funktion |

Welche Seite auf welcher Taste liegt, wird im Einstellungsfenster festgelegt. Es bildet die sechs
Tasten nach und zeigt eine pixelgenaue Vorschau des DED. Dort lässt sich auch die Sprache
(Deutsch/Englisch, Standard: Sprache des Betriebssystems) umstellen und die ICP-Testseite einblenden.

Seitentypen: System, Prozessor, Grafikkarte, Speicher, Laufwerke, Netzwerk, Uhr, RSS-Reader.
Der RSS-Reader liest RSS 2.0, RSS 1.0 und Atom, mischt mehrere Feeds nach Datum und lädt alle 10 Minuten neu.

## Pause für Flugsimulatoren

Solange ein Simulator läuft, überlässt ICP Monitor das ICP komplett dem Simulator bzw. SimAppPro:
Das DED zeigt drei Sekunden „DCS WORLD GESTARTET. ICP MONITOR WIRD PAUSIERT.“ und wird dann leer;
Tasten, Potis und Beleuchtung werden nicht mehr angefasst. Wenige Sekunden nach dem Beenden des
Simulators geht es automatisch weiter.

Erkannt werden DCS World, Microsoft Flight Simulator 2020/2024, Falcon BMS und X-Plane; SimAppPro
steht abwählbar in der Liste (standardmäßig aus, weil es oft dauerhaft im Hintergrund läuft).
Weitere Programme lassen sich im Einstellungsfenster (Reiter „Spiele“) hinzufügen – per exe-Datei
oder aus den gerade laufenden Programmen. Im Tray-Menü gibt es zusätzlich „Pausieren“ von Hand.

![Reiter „Spiele“ im Einstellungsfenster](docs/settings-games.png)

## Updates

Die App sucht beim Start und alle sechs Stunden auf GitHub nach einer neueren Version und meldet sie
am Tray-Symbol. Nach Bestätigung lädt sie den Installer herunter, prüft ihn gegen die mitveröffentlichte
SHA-256-Prüfsumme und installiert ihn ohne weitere Fragen; danach startet die neue Version. Ohne passende
Prüfsumme wird nichts installiert. Abschaltbar im Einstellungsfenster (Reiter „Allgemein“).

Ein Release braucht dafür beide Dateien aus `artifacts`: `ICP-Monitor-Setup-<Version>.exe` und
`ICP-Monitor-Setup-<Version>.exe.sha256` (erzeugt `build-installer.ps1`). Die Update-Suche funktioniert
nur, wenn das Repository öffentlich ist.

## ICP-Testseite

Zeigt die zuletzt gedrückte Taste, die Schalterstellungen und alle Potis. Zusätzlich ein Lichttest:
Der Dobber wählt einen Lichtkanal (0–15), das Poti `RET DEPR` setzt seinen Wert.

## Technik

- **ICP-Anbindung** über HidSharp, VID `0x4098`, PID `0xBF06`. Eingabe-Report `0x01` (Tasten als Bits,
  vier 12-Bit-Potis), Display über Output-Report `0xF0` (200×65 Pixel, 1 Bit pro Pixel), Licht über
  Output-Report `0x02` (`02 06 BF 00 00 03 49 <Kanal> <Wert>`). Kanal 0 = Tasten-LEDs, Kanal 1 = DED;
  einen Kanal für den Kontrast gibt es nicht. Die Kanäle stehen in `config.json` unter `lights`.
- **Sensoren** über LibreHardwareMonitorLib. Beim Start entsteht `sensors.txt` mit allen gefundenen Sensoren.
- **Schrift**: DED-Pixelschrift 8×13 aus [DedSharp](https://github.com/mnantel/dedcustomizer) (MIT, siehe `Assets/ded_font.LICENSE.txt`).
- **Log**: `%AppData%\ICP Monitor\icp-monitor.log`. `"logDisplay": true` in `config.json` schreibt zusätzlich jeden Displayinhalt ins Log.

## English summary

- **Install:** download `ICP-Monitor-Setup-<version>.exe` from the
  [releases](https://github.com/The-Real-G-Evil/icp-monitor/releases/latest) and run it. It bundles the
  .NET runtime and can set up autostart with administrator rights (needed for CPU temperature, power and
  fan sensors) via the Windows Task Scheduler, without a UAC prompt. The installer is not code-signed, so
  SmartScreen may warn on first run.
- **Use:** `COM1`, `COM2`, `IFF`, `LIST`, `A-A`, `A-G` select pages; which page sits on which button is set
  in the settings window (double-click the tray icon). Knob `SYM` = display brightness, `BRT` = button LEDs.
  The Dobber scrolls inside RSS and drive pages, `ENTR` opens the current RSS article.
- **Simulator pause:** while DCS World, MSFS 2020/2024, Falcon BMS, X-Plane or a program you add is running,
  the app shows a short notice and leaves the ICP entirely to the simulator.
- **Updates:** checks GitHub at startup and every six hours; installs only after confirmation and only if
  the SHA-256 checksum published with the release matches.
- **Language:** follows Windows (German or English), changeable in the settings window.
- **Build:** .NET 10 SDK, `dotnet build IcpMonitor.slnx`; installer with Inno Setup 6 via `.\build-installer.ps1`.

## Lizenz / License

MIT, siehe [LICENSE](LICENSE). Verwendete Fremdkomponenten: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
