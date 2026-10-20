using System.Globalization;
using IcpMonitor.Device;
using IcpMonitor.Display;
using IcpMonitor.Sensors;

namespace IcpMonitor.Pages;

/// <param name="SlotLabel">Taste, auf der die Seite liegt (z. B. "COM1").</param>
internal sealed record PageContext(
    SystemSnapshot Sensors,
    IcpInputSnapshot Input,
    bool DeviceConnected,
    string SlotLabel);

internal abstract class Page
{
    public abstract void Render(DedScreen screen, PageContext context);

    /// <summary>
    /// Seiteneigene Bedienung (z. B. Dobber, ENTR). Wird aus dem HID-Thread aufgerufen.
    /// Gibt true zurueck, wenn die Taste verbraucht wurde.
    /// </summary>
    public virtual bool HandleButton(IcpButton button) => false;

    /// <summary>Kopfzeile: Titel links (max. 13 Zeichen), Taste und Uhrzeit rechts.</summary>
    protected static void RenderHeader(DedScreen screen, PageContext context, string title)
    {
        string right = $"{context.SlotLabel} {DateTime.Now:HH:mm}";
        screen.Write(0, 0, DedText.Fit(title, DedScreen.Columns - right.Length - 1));
        screen.WriteRight(0, right);
    }

    protected static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    protected static string Percent(float? value) => value.HasValue ? $"{value.Value:0}%" : "--%";

    protected static string Temp(float? value) => value.HasValue ? $"{value.Value:0}C" : "--C";

    protected static string Watt(float? value) => value.HasValue ? $"{value.Value:0}W" : "--W";

    protected static string Rpm(float? value) => value.HasValue ? $"{value.Value:0}" : "--";

    /// <summary>Takt kompakt: 4512 MHz -> "4.5G", 45 MHz -> "45M".</summary>
    protected static string Ghz(float? mhz) => mhz switch
    {
        null => "--",
        < 1000 => mhz.Value.ToString("0", Inv) + "M",
        _ => (mhz.Value / 1000).ToString("0.0", Inv) + "G",
    };

    protected static string Gb(float? gb) => gb.HasValue ? gb.Value.ToString(gb.Value < 100 ? "0.0" : "0", Inv) : "--";

    /// <summary>"benutzt/gesamt" in GB, z. B. "18.7/93.7G".</summary>
    protected static string UsedOfTotal(float? usedGb, float? totalGb) => $"{Gb(usedGb)}/{Gb(totalGb)}G";

    /// <summary>Byte/s kompakt: 950K, 1.2M, 12M.</summary>
    protected static string Rate(float? bytesPerSecond)
    {
        if (!bytesPerSecond.HasValue) return "--";
        double v = bytesPerSecond.Value;
        if (v < 1_000_000) return (v / 1000).ToString("0", Inv) + "K";
        v /= 1_000_000;
        return v < 10 ? v.ToString("0.0", Inv) + "M" : v.ToString("0", Inv) + "M";
    }
}
