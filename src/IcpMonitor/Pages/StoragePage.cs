using IcpMonitor.Device;
using IcpMonitor.Display;
using IcpMonitor.Sensors;

namespace IcpMonitor.Pages;

/// <summary>
/// Laufwerke mit Belegung und freiem Platz, danach die physischen Datentraeger mit Temperatur und Aktivitaet:
/// <code>
/// LAUFWERKE 1/2   A-G 18:32
/// C:  45%   512G FREI
/// D:  80%   1.2T FREI
/// DATENTRAEGER  TEMP AKTIV
/// SAMSUNG 990  41C    3%
/// </code>
/// Mehr als vier Zeilen: mit dem Dobber blaettern.
/// </summary>
internal sealed class StoragePage : Page
{
    private const int LinesPerScreen = DedScreen.Rows - 1;
    private const int NameWidth = 12;
    private static readonly string[] NameNoise = ["SSD", "NVMe", "Solid State Drive", "(TM)", "(R)"];

    private readonly object sync = new();
    private int screenIndex;

    public override void Render(DedScreen screen, PageContext context)
    {
        List<string> lines = BuildLines(context.Sensors);
        int screens = Math.Max(1, (lines.Count + LinesPerScreen - 1) / LinesPerScreen);
        int current;
        lock (sync)
        {
            screenIndex = ((screenIndex % screens) + screens) % screens;
            current = screenIndex;
        }

        RenderHeader(screen, context, screens > 1 ? $"{L.DedDrives} {current + 1}/{screens}" : L.DedDrives);
        if (lines.Count == 0)
        {
            screen.Write(2, 0, L.DedNoDrives);
            return;
        }
        for (int i = 0; i < LinesPerScreen; i++)
        {
            int index = current * LinesPerScreen + i;
            if (index < lines.Count) screen.Write(1 + i, 0, lines[index]);
        }
    }

    public override bool HandleButton(IcpButton button)
    {
        int delta = button switch
        {
            IcpButton.DobberDown => +1,
            IcpButton.DobberUp => -1,
            _ => 0,
        };
        if (delta == 0) return false;
        lock (sync) screenIndex += delta;
        return true;
    }

    private static List<string> BuildLines(SystemSnapshot s)
    {
        var lines = s.Drives
            .OrderBy(d => d.Letter)
            .Select(d => $"{d.Letter}: {Percent((float)d.UsedPercent),4}  {Size(d.FreeGb),5} {L.DedFree}")
            .ToList();

        var disks = s.Disks.Where(d => d.Temp.HasValue || d.ActivityPercent.HasValue).ToList();
        if (disks.Count > 0)
        {
            lines.Add(L.DedDisksHeader);
            lines.AddRange(disks.Select(d => $"{ShortName(d.Name),-NameWidth} {Temp(d.Temp),4} {Percent(d.ActivityPercent),5}"));
        }
        return lines;
    }

    /// <summary>GB kompakt: 512G, 1.2T.</summary>
    private static string Size(double gb) =>
        gb < 1000 ? gb.ToString("0", Inv) + "G" : (gb / 1000).ToString("0.0", Inv) + "T";

    private static string ShortName(string name)
    {
        foreach (string noise in NameNoise) name = name.Replace(noise, " ", StringComparison.OrdinalIgnoreCase);
        return DedText.Fit(DedText.Normalize(name), NameWidth);
    }
}
