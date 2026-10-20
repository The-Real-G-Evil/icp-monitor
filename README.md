# ICP Monitor

Zeigt PC-Sensorwerte auf dem DED-Display eines Winwing/WinCTRL F-16 ICP an. Läuft im Hintergrund
mit Symbol im Infobereich der Taskleiste und wird über die Tasten und Potis des ICP bedient.

## Installation

`artifacts\ICP-Monitor-Setup-<Version>.exe` ausführen. Der Installer bringt die .NET-Laufzeit mit,
installiert nach `C:\Program Files\ICP Monitor`, legt einen Startmenü-Eintrag an und richtet auf
Wunsch den Autostart ein. Eine laufende Instanz wird beim Update automatisch beendet.

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
