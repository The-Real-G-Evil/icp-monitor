using IcpMonitor.Device;
using IcpMonitor.Display;

namespace IcpMonitor.Pages;

/// <summary>
/// Testseite fuer die Bedienelemente und die Lichtkanaele des ICP:
/// <code>
/// ICP NORM LVL   TEST 14:05
/// LETZTE: COM1
/// LICHT K2     55%
/// SYM  20%   RET    0%
/// BRT 100%   CONT  55%
/// </code>
/// Lichttest: Dobber waehlt den Kanal, das Poti RET DEPR setzt seinen Wert
/// (das Senden uebernimmt <see cref="MonitorApp"/>).
/// </summary>
internal sealed class IcpPage : Page
{
    public const int MaxTestChannel = 15;

    private int testChannel = 2;

    /// <summary>Kanal, der per RET DEPR gesteuert wird.</summary>
    public int TestChannel => Volatile.Read(ref testChannel);

    public override void Render(DedScreen screen, PageContext context)
    {
        var input = context.Input;
        RenderHeader(screen, context, $"ICP {input.DriftNorm} {input.GainLvlAuto}");
        screen.Write(1, 0, L.DedLastInput + input.LastEvent);
        screen.Write(2, 0, L.DedLightChannel(TestChannel, input.Poti(IcpPoti.RetDepr)));
        screen.Write(3, 0, $"SYM {input.Poti(IcpPoti.Sym),3}%   RET  {input.Poti(IcpPoti.RetDepr),3}%");
        screen.Write(4, 0, $"BRT {input.Poti(IcpPoti.Brt),3}%   CONT {input.Poti(IcpPoti.Cont),3}%");
    }

    public override bool HandleButton(IcpButton button)
    {
        int delta = button switch
        {
            IcpButton.DobberUp => +1,
            IcpButton.DobberDown => -1,
            _ => 0,
        };
        if (delta == 0) return false;
        Volatile.Write(ref testChannel, Math.Clamp(TestChannel + delta, 0, MaxTestChannel));
        return true;
    }
}
