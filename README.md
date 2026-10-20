# ICP Monitor

Zeigt PC-Sensorwerte auf dem DED-Display eines Winwing/WinCTRL F-16 ICP an. Läuft im Hintergrund
mit Symbol im Infobereich der Taskleiste und wird über die Tasten und Potis des ICP bedient.

## Bauen und starten

Voraussetzung: .NET 10 SDK.

```
dotnet build IcpMonitor.slnx
src\IcpMonitor\bin\Debug\net10.0-windows\win-x64\IcpMonitor.exe
```

Als Administrator gestartet liefert die App zusätzlich CPU-Temperatur, CPU-Leistung, Takt,
Mainboard-Lüfter und Datenträger-Temperaturen (Tray-Menü > „Als Administrator neu starten“).

Startparameter: `--settings` öffnet direkt das Einstellungsfenster.

## Bedienung

Tray-Symbol: Linksklick = nächste Seite, Doppelklick = Einstellungen, Rechtsklick = Menü.

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
- **Log**: `icp-monitor.log` neben der exe. `"logDisplay": true` in `config.json` schreibt zusätzlich jeden Displayinhalt ins Log.
