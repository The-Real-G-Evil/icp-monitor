using HidSharp;

namespace IcpMonitor.Device;

/// <summary>
/// Verbindung zum ICP per HID: liest Eingabe-Reports in einem eigenen Thread,
/// schreibt Bilder auf das Display und verbindet sich nach Abstecken selbst neu.
/// </summary>
internal sealed class IcpDevice : IDisposable
{
    private const int MinDisplayReportLength = 64;
    private const int MinInputReportLength = 14;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource cts = new();
    private readonly object writeLock = new();
    private Thread? thread;
    private HidStream? inputStream;
    private HidStream? outputStream;
    private int outputReportLength = MinDisplayReportLength;
    private byte sequence = 1;
    private bool lastAttemptFailed;

    /// <summary>Wird im Lese-Thread mit jedem empfangenen Eingabe-Report aufgerufen.</summary>
    public event Action<byte[]>? InputReport;

    public event Action<bool>? ConnectionChanged;

    public bool IsConnected { get; private set; }

    public void Start()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "ICP-HID" };
        thread.Start();
    }

    /// <summary>Sendet ein komplettes Bild. Gibt false zurueck, wenn kein Display verbunden ist.</summary>
    public bool WriteFrame(byte[] pixelBuffer)
    {
        lock (writeLock)
        {
            if (outputStream == null) return false;
            try
            {
                foreach (byte[] report in IcpProtocol.BuildFrameReports(pixelBuffer, outputReportLength, ref sequence))
                {
                    outputStream.Write(report);
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or TimeoutException)
            {
                Log.Warn($"Schreiben auf das ICP fehlgeschlagen: {ex.Message}");
                // Schliessen beendet auch die Lese-Schleife, die dann neu verbindet.
                CloseStreams();
                return false;
            }
        }
    }

    /// <summary>Setzt einen Lichtkanal (0-255). Gibt false zurueck, wenn kein ICP verbunden ist.</summary>
    public bool WriteLight(byte channel, byte value)
    {
        lock (writeLock)
        {
            if (outputStream == null) return false;
            try
            {
                outputStream.Write(IcpProtocol.BuildLightReport(outputReportLength, channel, value));
                return true;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or TimeoutException)
            {
                Log.Warn($"Lichtkommando fehlgeschlagen: {ex.Message}");
                return false;
            }
        }
    }

    private void Run()
    {
        while (!cts.IsCancellationRequested)
        {
            try
            {
                if (Connect()) ReadLoop();
            }
            catch (Exception ex)
            {
                if (!cts.IsCancellationRequested) Log.Warn($"ICP-Verbindung unterbrochen: {ex.Message}");
            }
            finally
            {
                lock (writeLock) CloseStreams();
                SetConnected(false);
            }
            cts.Token.WaitHandle.WaitOne(RetryDelay);
        }
    }

    private bool Connect()
    {
        var devices = DeviceList.Local.GetHidDevices(IcpProtocol.VendorId, IcpProtocol.ProductId).ToList();
        HidDevice? display = devices.FirstOrDefault(d => SafeLength(d.GetMaxOutputReportLength) >= MinDisplayReportLength);
        HidDevice? input = devices.FirstOrDefault(d => SafeLength(d.GetMaxInputReportLength) >= MinInputReportLength);

        if (display == null || input == null)
        {
            if (!lastAttemptFailed)
            {
                Log.Warn(devices.Count == 0
                    ? "ICP nicht gefunden (VID 4098, PID BF06). Neuer Versuch alle 2 s."
                    : $"ICP gefunden, aber unpassende HID-Schnittstellen ({devices.Count}). Display: {display != null}, Eingabe: {input != null}");
                lastAttemptFailed = true;
            }
            return false;
        }

        HidStream inStream = input.Open();
        inStream.ReadTimeout = 1000;
        HidStream outStream = ReferenceEquals(display, input) ? inStream : display.Open();

        lock (writeLock)
        {
            inputStream = inStream;
            outputStream = outStream;
            outputReportLength = SafeLength(display.GetMaxOutputReportLength);
            sequence = 1;
            outputStream.Write(IcpProtocol.BuildInitReport(outputReportLength));
        }

        lastAttemptFailed = false;
        Log.Info($"ICP verbunden: {input.DevicePath} (Eingabe {input.GetMaxInputReportLength()} B, Ausgabe {outputReportLength} B)");
        SetConnected(true);
        return true;
    }

    private void ReadLoop()
    {
        HidStream stream = inputStream!;
        var buffer = new byte[Math.Max(MinInputReportLength, stream.Device.GetMaxInputReportLength())];
        while (!cts.IsCancellationRequested)
        {
            int count;
            try
            {
                count = stream.Read(buffer, 0, buffer.Length);
            }
            catch (TimeoutException)
            {
                continue;
            }
            if (count > 0) InputReport?.Invoke(buffer[..count]);
        }
    }

    private void CloseStreams()
    {
        if (outputStream != null && !ReferenceEquals(outputStream, inputStream)) outputStream.Dispose();
        inputStream?.Dispose();
        inputStream = null;
        outputStream = null;
    }

    private void SetConnected(bool connected)
    {
        if (IsConnected == connected) return;
        IsConnected = connected;
        if (!connected) Log.Info("ICP getrennt.");
        ConnectionChanged?.Invoke(connected);
    }

    private static int SafeLength(Func<int> getter)
    {
        try { return getter(); }
        catch { return 0; }
    }

    public void Dispose()
    {
        cts.Cancel();
        lock (writeLock) CloseStreams();
        thread?.Join(TimeSpan.FromSeconds(2));
        cts.Dispose();
    }
}
