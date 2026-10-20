using IcpMonitor.Display;

namespace IcpMonitor.Pages;

/// <summary>
/// <code>
/// CPU            COM2 18:32
/// LAST  23%  KERN  78%
/// TEMP  54C  TAKT  4.5G
/// LEIST  65W  THREADS 32
/// RYZEN 9 9950X3D
/// </code>
/// </summary>
internal sealed class CpuPage : Page
{
    private static readonly string[] NameNoise =
        ["AMD ", "Intel(R) ", "Core(TM) ", "(R)", "(TM)", " Processor", " CPU", "-Core"];

    public override void Render(DedScreen screen, PageContext context)
    {
        var s = context.Sensors;
        RenderHeader(screen, context, "CPU");
        screen.Write(1, 0, $"{L.DedLoad} {Percent(s.CpuLoad),4}  {L.DedCore} {Percent(s.CpuMaxCoreLoad),4}");
        screen.Write(2, 0, $"TEMP {Temp(s.CpuTemp),4}  {L.DedClockSpeed} {Ghz(s.CpuClockMhz),5}");
        screen.Write(3, 0, $"{L.DedPower}{Watt(s.CpuPower),5}  THREADS {Environment.ProcessorCount}");
        screen.Write(4, 0, DedText.Fit(DedText.Normalize(ShortName(s.CpuName)), DedScreen.Columns));
    }

    private static string ShortName(string name)
    {
        foreach (string noise in NameNoise) name = name.Replace(noise, " ", StringComparison.OrdinalIgnoreCase);
        int at = name.IndexOf('@');
        return (at >= 0 ? name[..at] : name).Trim();
    }
}

/// <summary>
/// <code>
/// GPU             IFF 18:32
/// LAST   5%  TAKT  2.5G
/// TEMP  41C  HOT   52C
/// LEIST 120W  LUEFT 1200
/// VRAM 0.9/24.0G       4%
/// </code>
/// </summary>
internal sealed class GpuPage : Page
{
    public override void Render(DedScreen screen, PageContext context)
    {
        var s = context.Sensors;
        RenderHeader(screen, context, "GPU");
        screen.Write(1, 0, $"{L.DedLoad} {Percent(s.GpuLoad),4}  {L.DedClockSpeed} {Ghz(s.GpuClockMhz),5}");
        screen.Write(2, 0, $"TEMP {Temp(s.GpuTemp),4}  HOT  {Temp(s.GpuHotSpot),4}");
        screen.Write(3, 0, $"{L.DedPower}{Watt(s.GpuPower),5}  {L.DedFan}{Rpm(s.GpuFanRpm),5}");
        screen.Write(4, 0, "VRAM " + UsedOfTotal(s.GpuMemUsedMb / 1024, s.GpuMemTotalMb / 1024));
        screen.WriteRight(4, Percent(s.GpuMemUsedMb / s.GpuMemTotalMb * 100));
    }
}

/// <summary>
/// <code>
/// SPEICHER       LIST 18:32
/// RAM  18.7/93.7G      20%
/// FREI 75.0G
/// VIRT 21.7/99.7G      22%
/// VRAM 0.9/24.0G        4%
/// </code>
/// </summary>
internal sealed class MemoryPage : Page
{
    public override void Render(DedScreen screen, PageContext context)
    {
        var s = context.Sensors;
        RenderHeader(screen, context, L.DedMemory);

        screen.Write(1, 0, "RAM  " + UsedOfTotal(s.RamUsedGb, s.RamUsedGb + s.RamAvailableGb));
        screen.WriteRight(1, Percent(s.RamLoad));
        screen.Write(2, 0, $"{L.DedFree} {Gb(s.RamAvailableGb)}G");

        screen.Write(3, 0, "VIRT " + UsedOfTotal(s.VirtUsedGb, s.VirtUsedGb + s.VirtAvailableGb));
        screen.WriteRight(3, Percent(s.VirtLoad));

        screen.Write(4, 0, "VRAM " + UsedOfTotal(s.GpuMemUsedMb / 1024, s.GpuMemTotalMb / 1024));
        screen.WriteRight(4, Percent(s.GpuMemUsedMb / s.GpuMemTotalMb * 100));
    }
}

/// <summary>
/// <code>
/// NETZWERK        A-A 18:32
/// DOWN  1.2 MB/S
/// UP    120 KB/S
/// GESAMT D 1.9G  U 0.1G
/// ETHERNET
/// </code>
/// </summary>
internal sealed class NetworkPage : Page
{
    public override void Render(DedScreen screen, PageContext context)
    {
        var s = context.Sensors;
        RenderHeader(screen, context, L.DedNetwork);
        screen.Write(1, 0, $"{L.DedDown}  {RateLong(s.NetDownBps)}");
        screen.Write(2, 0, $"{L.DedUp}  {RateLong(s.NetUpBps)}");
        screen.Write(3, 0, $"{L.DedTotal} D {Gb(s.NetDownTotalGb)}G  U {Gb(s.NetUpTotalGb)}G");
        screen.Write(4, 0, DedText.Fit(DedText.Normalize(s.NetAdapter), DedScreen.Columns));
    }

    private static string RateLong(float? bytesPerSecond)
    {
        if (!bytesPerSecond.HasValue) return "--";
        double v = bytesPerSecond.Value;
        if (v < 1_000_000) return (v / 1000).ToString("0", Inv) + " KB/S";
        v /= 1_000_000;
        return v.ToString(v < 100 ? "0.0" : "0", Inv) + " MB/S";
    }
}

/// <summary>
/// <code>
/// UHR             A-G 18:32
///         DIENSTAG
///        20.10.2026
///         18:32:45
/// LAUFZEIT 3T 04:12
/// </code>
/// </summary>
internal sealed class ClockPage : Page
{
    public override void Render(DedScreen screen, PageContext context)
    {
        DateTime now = DateTime.Now;
        RenderHeader(screen, context, L.DedClock);
        screen.WriteCentered(1, DedText.Normalize(now.ToString("dddd", L.Culture)));
        screen.WriteCentered(2, now.ToString(L.Culture.DateTimeFormat.ShortDatePattern, L.Culture));
        screen.WriteCentered(3, now.ToString("HH:mm:ss", Inv));

        TimeSpan up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        screen.Write(4, 0, up.TotalDays >= 1
            ? $"{L.DedUptime} {(int)up.TotalDays}{L.DedDayShort} {up:hh\\:mm}"
            : $"{L.DedUptime} {up:hh\\:mm}");
    }
}
