namespace IcpMonitor.Device;

/// <summary>
/// HID-Protokoll des Winwing ICP/DED-Displays.
/// Ein Bild wird als zwei Befehle (Display-Speicher schreiben, Refresh) gesendet,
/// die in 60-Byte-Stuecke zerlegt und in 64-Byte-Reports mit ID 0xF0 verpackt werden.
/// </summary>
internal static class IcpProtocol
{
    public const int VendorId = 0x4098;
    public const int ProductId = 0xBF06;

    public const int DisplayWidth = 200;
    public const int DisplayHeight = 65;
    public const int BytesPerRow = DisplayWidth / 8;

    /// <summary>Groesse des Pixelpuffers: 4 Byte Vorspann + 1 Bit pro Pixel.</summary>
    public const int PixelBufferLength = 4 + BytesPerRow * DisplayHeight;

    private const int ReportLength = 64;
    private const int PayloadLength = 60;
    private const byte OutputReportId = 0xF0;
    private const byte LightReportId = 0x02;
    private const byte OpInit = 0x02;
    private const uint CmdWriteDisplayMem = 0x102;
    private const uint CmdRefreshDisplay = 0x103;
    private const uint CommandTimestamp = 0xFFFF;

    /// <summary>Index des ersten Pixelbytes von Zeile <paramref name="y"/> im Pixelpuffer.</summary>
    public static int RowOffset(int y) => 4 + y * BytesPerRow;

    public static byte[] BuildInitReport(int reportLength) => BuildReport(reportLength, OpInit, 0, [0]);

    /// <summary>
    /// Licht-/Helligkeitskommando, wie es auch andere Winwing-Geraete (MCDU, PAP3) verwenden:
    /// 02 &lt;PID lo&gt; &lt;PID hi&gt; 00 00 03 49 &lt;Kanal&gt; &lt;Wert 0-255&gt;.
    /// Welcher Kanal beim ICP was steuert, ist nicht dokumentiert - siehe config.json "lights".
    /// </summary>
    public static byte[] BuildLightReport(int reportLength, byte channel, byte value)
    {
        var report = new byte[Math.Max(ReportLength, reportLength)];
        report[0] = LightReportId;
        report[1] = ProductId & 0xFF;
        report[2] = ProductId >> 8;
        report[5] = 0x03;
        report[6] = 0x49;
        report[7] = channel;
        report[8] = value;
        return report;
    }

    /// <summary>Zerlegt ein komplettes Bild in die zu sendenden HID-Reports.</summary>
    public static List<byte[]> BuildFrameReports(byte[] pixelBuffer, int reportLength, ref byte sequence)
    {
        byte[] write = BuildCommand(CmdWriteDisplayMem, pixelBuffer);
        byte[] refresh = BuildCommand(CmdRefreshDisplay, [0]);
        byte[] stream = [.. write, .. refresh];

        var reports = new List<byte[]>(stream.Length / PayloadLength + 1);
        for (int start = 0; start < stream.Length; start += PayloadLength)
        {
            int count = Math.Min(PayloadLength, stream.Length - start);
            reports.Add(BuildReport(reportLength, 0, sequence++, stream.AsSpan(start, count)));
        }
        return reports;
    }

    private static byte[] BuildCommand(uint commandType, byte[] data)
    {
        // Aufbau: ProductId(4) Befehl(4) Zeitstempel(4) 0x00(1) Laenge(4) Daten(n)
        var output = new byte[17 + data.Length];
        BitConverter.TryWriteBytes(output.AsSpan(0, 4), (uint)ProductId);
        BitConverter.TryWriteBytes(output.AsSpan(4, 4), commandType);
        BitConverter.TryWriteBytes(output.AsSpan(8, 4), CommandTimestamp);
        BitConverter.TryWriteBytes(output.AsSpan(13, 4), data.Length);
        data.CopyTo(output, 17);
        return output;
    }

    private static byte[] BuildReport(int reportLength, byte opType, byte sequence, ReadOnlySpan<byte> payload)
    {
        var report = new byte[Math.Max(ReportLength, reportLength)];
        report[0] = OutputReportId;
        report[1] = opType;
        report[2] = sequence;
        report[3] = (byte)payload.Length;
        payload.CopyTo(report.AsSpan(4));
        return report;
    }
}
