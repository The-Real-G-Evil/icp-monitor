namespace IcpMonitor.Device;

internal enum IcpButton
{
    Com1, Com2, Iff, List, AA, AG,
    Key1, Key2, Key3, Key4, Key5, Key6, Key7, Key8, Key9, Key0,
    Rcl, Entr,
    DobberUp, DobberDown,
    Up, Down, Seq, Rtn,
    WarnReset, Wx, FlirUp, FlirDown,
}

internal enum IcpPoti { RetDepr, Sym, Cont, Brt }

internal readonly record struct ButtonBit(IcpButton Button, string Label, int ByteIndex, byte Mask);

/// <summary>Lage der Tasten, Schalter und Potis im Eingabe-Report (aus der Kalibrierung vom 04.07.2026).</summary>
internal static class IcpLayout
{
    public const byte InputReportId = 0x01;
    public const int MinReportLength = 14;

    public static readonly ButtonBit[] Buttons =
    [
        new(IcpButton.Com1, "COM1", 1, 0x01),
        new(IcpButton.Com2, "COM2", 1, 0x02),
        new(IcpButton.Iff, "IFF", 1, 0x04),
        new(IcpButton.List, "LIST", 1, 0x08),
        new(IcpButton.AA, "A-A", 1, 0x10),
        new(IcpButton.AG, "A-G", 1, 0x20),
        new(IcpButton.Key1, "1", 1, 0x40),
        new(IcpButton.Key2, "2", 1, 0x80),
        new(IcpButton.Key3, "3", 2, 0x01),
        new(IcpButton.Rcl, "RCL", 2, 0x02),
        new(IcpButton.Key4, "4", 2, 0x04),
        new(IcpButton.Key5, "5", 2, 0x08),
        new(IcpButton.Key6, "6", 2, 0x10),
        new(IcpButton.Entr, "ENTR", 2, 0x20),
        new(IcpButton.Key7, "7", 2, 0x40),
        new(IcpButton.Key8, "8", 2, 0x80),
        new(IcpButton.Key9, "9", 3, 0x01),
        new(IcpButton.Key0, "0", 3, 0x02),
        new(IcpButton.DobberUp, "DOBBER UP", 3, 0x04),
        new(IcpButton.DobberDown, "DOBBER DN", 3, 0x08),
        new(IcpButton.Up, "UP", 3, 0x20),
        new(IcpButton.Seq, "SEQ", 3, 0x40),
        new(IcpButton.Down, "DOWN", 3, 0x80),
        new(IcpButton.Rtn, "RTN", 4, 0x01),
        new(IcpButton.WarnReset, "WARN RESET", 4, 0x08),
        new(IcpButton.Wx, "WX", 4, 0x10),
        new(IcpButton.FlirUp, "FLIR UP", 4, 0x20),
        new(IcpButton.FlirDown, "FLIR DN", 4, 0x40),
    ];

    /// <summary>Byte-Offsets der vier 12-Bit-Potis (Little Endian), Reihenfolge wie <see cref="IcpPoti"/>.</summary>
    public static readonly int[] PotiOffsets = [6, 8, 10, 12];

    public const int PotiMax = 4095;

    // DRIFT/NORM und GAIN/LVL/AUTO liegen als Einzelbits in Byte 4 und 5.
    public const int SwitchByteA = 4;
    public const int SwitchByteB = 5;
    public const byte DriftBit = 0x02;
    public const byte NormBit = 0x04;
    public const byte GainBit = 0x80;
    public const byte LvlBit = 0x01;
    public const byte AutoBit = 0x02;

    public static string Label(IcpButton button) => Array.Find(Buttons, b => b.Button == button).Label;

    /// <summary>Ziffer 0-9 einer Zifferntaste, sonst null.</summary>
    public static int? Digit(IcpButton button) => button switch
    {
        >= IcpButton.Key1 and <= IcpButton.Key9 => button - IcpButton.Key1 + 1,
        IcpButton.Key0 => 0,
        _ => null,
    };
}

/// <summary>Unveraenderlicher Zustand aller Bedienelemente, sicher zwischen Threads teilbar.</summary>
internal sealed record IcpInputSnapshot(
    IReadOnlySet<IcpButton> Pressed,
    IReadOnlyList<int> PotiPercent,
    string DriftNorm,
    string GainLvlAuto,
    string LastEvent)
{
    public static readonly IcpInputSnapshot Empty =
        new(new HashSet<IcpButton>(), [0, 0, 0, 0], "-", "-", "-");

    public int Poti(IcpPoti poti) => PotiPercent[(int)poti];
}

/// <summary>Wertet Eingabe-Reports aus und meldet Aenderungen als Ereignisse (Flanken, nicht Pegel).</summary>
internal sealed class IcpInputDecoder
{
    // Rauschunterdrueckung: erst ab dieser Aenderung des Rohwerts gilt ein Poti als bewegt.
    private const int PotiDeadband = 40;

    private readonly object sync = new();
    private readonly int[] potiRaw = [-1, -1, -1, -1];
    private HashSet<IcpButton> pressed = new();

    public event Action<IcpButton>? ButtonPressed;

    public event Action<IcpPoti, int>? PotiChanged;

    public event Action<string, string>? SwitchChanged;

    public IcpInputSnapshot Current { get; private set; } = IcpInputSnapshot.Empty;

    public void Process(byte[] report)
    {
        if (report.Length < IcpLayout.MinReportLength || report[0] != IcpLayout.InputReportId) return;

        var newlyPressed = new List<IcpButton>();
        var changedPotis = new List<(IcpPoti, int)>();
        bool switchChanged;
        IcpInputSnapshot snapshot;

        lock (sync)
        {
            var nowPressed = new HashSet<IcpButton>();
            foreach (ButtonBit bit in IcpLayout.Buttons)
            {
                if ((report[bit.ByteIndex] & bit.Mask) == 0) continue;
                nowPressed.Add(bit.Button);
                if (!pressed.Contains(bit.Button)) newlyPressed.Add(bit.Button);
            }
            pressed = nowPressed;

            int[] percent = new int[potiRaw.Length];
            for (int i = 0; i < potiRaw.Length; i++)
            {
                int offset = IcpLayout.PotiOffsets[i];
                int raw = Math.Clamp(report[offset] | (report[offset + 1] << 8), 0, IcpLayout.PotiMax);
                bool first = potiRaw[i] < 0;
                if (first || Math.Abs(raw - potiRaw[i]) >= PotiDeadband)
                {
                    potiRaw[i] = raw;
                    if (!first) changedPotis.Add(((IcpPoti)i, ToPercent(raw)));
                }
                percent[i] = ToPercent(potiRaw[i]);
            }

            byte a = report[IcpLayout.SwitchByteA];
            byte b = report[IcpLayout.SwitchByteB];
            string drift = (a & IcpLayout.DriftBit) != 0 ? "DRIFT" : (a & IcpLayout.NormBit) != 0 ? "NORM" : Current.DriftNorm;
            string gain = (a & IcpLayout.GainBit) != 0 ? "GAIN"
                : (b & IcpLayout.LvlBit) != 0 ? "LVL"
                : (b & IcpLayout.AutoBit) != 0 ? "AUTO"
                : Current.GainLvlAuto;
            switchChanged = drift != Current.DriftNorm || gain != Current.GainLvlAuto;

            string lastEvent = Current.LastEvent;
            if (newlyPressed.Count > 0) lastEvent = IcpLayout.Label(newlyPressed[^1]);
            else if (changedPotis.Count > 0) lastEvent = $"{changedPotis[^1].Item1.ToString().ToUpperInvariant()} {changedPotis[^1].Item2}%";
            else if (switchChanged && !ReferenceEquals(Current, IcpInputSnapshot.Empty)) lastEvent = $"{drift} {gain}";

            snapshot = new IcpInputSnapshot(nowPressed, percent, drift, gain, lastEvent);
            Current = snapshot;
        }

        foreach (IcpButton button in newlyPressed) ButtonPressed?.Invoke(button);
        foreach ((IcpPoti poti, int value) in changedPotis) PotiChanged?.Invoke(poti, value);
        if (switchChanged) SwitchChanged?.Invoke(snapshot.DriftNorm, snapshot.GainLvlAuto);
    }

    private static int ToPercent(int raw) => (int)Math.Round(raw * 100.0 / IcpLayout.PotiMax);
}
