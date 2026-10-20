using IcpMonitor.Display;

namespace IcpMonitor.Pages;

/// <summary>
/// Die wichtigsten Werte auf einen Blick (Last, Temperatur, Leistung, Takt):
/// <code>
/// SYSTEM         COM1 14:05
/// CPU  23%  54C  65W  4.5G
/// GPU   5%  41C 120W  2.5G
/// RAM  18.9/93.7G      20%
/// VRAM 1.2/24.0G        5%
/// </code>
/// </summary>
internal sealed class SystemPage : Page
{
    public override void Render(DedScreen screen, PageContext context)
    {
        var s = context.Sensors;
        RenderHeader(screen, context, L.DedSystem);

        screen.Write(1, 0, $"CPU {Percent(s.CpuLoad),4} {Temp(s.CpuTemp),4} {Watt(s.CpuPower),4} {Ghz(s.CpuClockMhz),5}");
        screen.Write(2, 0, $"GPU {Percent(s.GpuLoad),4} {Temp(s.GpuTemp),4} {Watt(s.GpuPower),4} {Ghz(s.GpuClockMhz),5}");

        screen.Write(3, 0, "RAM  " + UsedOfTotal(s.RamUsedGb, s.RamUsedGb + s.RamAvailableGb));
        screen.WriteRight(3, Percent(s.RamLoad));

        screen.Write(4, 0, "VRAM " + UsedOfTotal(s.GpuMemUsedMb / 1024, s.GpuMemTotalMb / 1024));
        screen.WriteRight(4, Percent(s.GpuMemUsedMb / s.GpuMemTotalMb * 100));
    }
}
