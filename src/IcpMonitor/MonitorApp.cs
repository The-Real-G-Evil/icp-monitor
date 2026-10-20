using System.Diagnostics;
using IcpMonitor.Device;
using IcpMonitor.Display;
using IcpMonitor.Pages;
using IcpMonitor.Rss;
using IcpMonitor.Sensors;

namespace IcpMonitor;

/// <summary>Eine Seitentaste des ICP mit der ihr zugewiesenen Seite (null = nicht belegt).</summary>
internal sealed record Slot(string Label, IcpButton Button, SlotConfig Config, Page? Page);

/// <summary>
/// Kern der Anwendung: verbindet ICP, Sensoren und Seiten, verarbeitet Tasten
/// und zeichnet das Display in einem eigenen Thread.
/// </summary>
internal sealed class MonitorApp : IDisposable
{
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(5);

    /// <summary>Physische Tasten zu <see cref="AppConfig.SlotButtons"/>, gleiche Reihenfolge.</summary>
    private static readonly IcpButton[] SlotIcpButtons =
        [IcpButton.Com1, IcpButton.Com2, IcpButton.Iff, IcpButton.List, IcpButton.AA, IcpButton.AG];

    private readonly IcpDevice device = new();
    private readonly IcpInputDecoder input = new();
    private readonly SensorService sensors;
    private readonly AutoResetEvent wake = new(false);
    private readonly CancellationTokenSource cts = new();
    private readonly object sync = new();
    private Thread? renderThread;

    private AppConfig config;
    private Slot[] slots;
    private int slotIndex;
    private bool displayOn = true;
    private string? statusMessage;
    private DateTime statusUntil;

    /// <summary>Seite, die ueber den Slots liegt (ICP-Test); null = normale Slot-Anzeige.</summary>
    private Page? overlayPage;

    // Zuletzt gesendete Lichtwerte je Kanal; nach (Wieder-)Verbinden geleert, damit alles neu gesendet wird.
    private readonly Dictionary<int, byte> sentLights = new();
    private volatile bool lightsPending = true;

    public MonitorApp(AppConfig config)
    {
        this.config = config;
        sensors = new SensorService(config.SensorIntervalMs);
        slots = BuildSlots(config);
        slotIndex = Math.Max(0, Array.FindIndex(slots, s => s.Label.Equals(config.StartSlot, StringComparison.OrdinalIgnoreCase)));

        device.InputReport += report =>
        {
            input.Process(report);
            // Erst nach dem ersten Report sind die Poti-Stellungen bekannt.
            if (lightsPending) ApplyLights();
        };
        device.ConnectionChanged += connected =>
        {
            if (connected)
            {
                lock (sentLights) sentLights.Clear();
                lightsPending = true;
            }
            wake.Set();
            OnStateChanged();
        };
        input.ButtonPressed += HandleButton;
        input.PotiChanged += (poti, _) =>
        {
            if (poti is IcpPoti.Brt or IcpPoti.Sym or IcpPoti.Cont) ApplyLights();
            else if (poti == IcpPoti.RetDepr) SendTestLight();
            wake.Set();
        };
        input.SwitchChanged += (_, _) => wake.Set();
    }

    /// <summary>Wird bei Seitenwechsel, Display an/aus, neuer Konfiguration oder Verbindungsaenderung ausgeloest (beliebiger Thread).</summary>
    public event Action? StateChanged;

    public RssService Rss { get; } = new();

    public IReadOnlyList<Slot> Slots { get { lock (sync) return slots; } }

    public int SlotIndex { get { lock (sync) return slotIndex; } }

    public bool DisplayOn { get { lock (sync) return displayOn; } }

    public bool DeviceConnected => device.IsConnected;

    public SystemSnapshot Sensors => sensors.Current;

    public bool TestPageActive { get { lock (sync) return overlayPage != null; } }

    /// <summary>Blendet die ICP-Testseite ein oder aus. Jede Seitentaste am ICP blendet sie ebenfalls aus.</summary>
    public void ToggleTestPage()
    {
        bool closed;
        lock (sync)
        {
            closed = overlayPage != null;
            overlayPage = closed ? null : new IcpPage();
        }
        if (closed) RestoreLights();
        wake.Set();
        OnStateChanged();
    }

    /// <summary>Lichttest der ICP-Testseite: den gewaehlten Kanal auf die Stellung von RET DEPR setzen.</summary>
    private void SendTestLight()
    {
        IcpPage? test;
        lock (sync) test = overlayPage as IcpPage;
        if (test == null) return;
        int percent = input.Current.Poti(IcpPoti.RetDepr);
        if (device.WriteLight((byte)test.TestChannel, (byte)Math.Round(percent * 2.55)))
        {
            Log.Info($"Lichttest: Kanal {test.TestChannel} = {percent}%");
        }
    }

    /// <summary>Nach dem Lichttest die normalen Kanaele wieder auf die Poti-Stellungen setzen.</summary>
    private void RestoreLights()
    {
        lock (sentLights) sentLights.Clear();
        ApplyLights();
    }

    /// <summary>Kopie der aktuellen Konfiguration zum Bearbeiten.</summary>
    public AppConfig CloneConfig()
    {
        lock (sync) return config.Clone();
    }

    public void Start()
    {
        sensors.Start();
        device.Start();
        renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "DED-Render" };
        renderThread.Start();
    }

    /// <summary>Uebernimmt eine geaenderte Konfiguration, speichert sie und baut die Seiten neu auf.</summary>
    public void ApplyConfig(AppConfig newConfig)
    {
        newConfig.Save();
        L.SetLanguage(newConfig.Language);
        Slot[] newSlots = BuildSlots(newConfig);
        lock (sync)
        {
            config = newConfig;
            slots = newSlots;
        }
        // Lichtkanaele koennten sich geaendert haben - alles neu senden.
        lock (sentLights) sentLights.Clear();
        ApplyLights();
        wake.Set();
        OnStateChanged();
    }

    public Page? CreatePage(SlotConfig slot) => PageCatalog.Get(slot.Page).Create(slot, Rss);

    /// <summary>Zeichnet eine Seite so, wie sie auf dem ICP erscheinen wuerde (fuer die Vorschau).</summary>
    public DedScreen RenderPreview(Page? page, string slotLabel)
    {
        var screen = new DedScreen();
        var context = new PageContext(sensors.Current, input.Current, device.IsConnected, slotLabel);
        if (page != null) page.Render(screen, context);
        else RenderEmpty(screen, slotLabel);
        return screen;
    }

    public void SelectSlot(int index)
    {
        bool closedTest;
        lock (sync)
        {
            slotIndex = ((index % slots.Length) + slots.Length) % slots.Length;
            closedTest = overlayPage != null;
            overlayPage = null;
        }
        if (closedTest) RestoreLights();
        wake.Set();
        OnStateChanged();
    }

    /// <summary>Naechste/vorige belegte Seite; nicht belegte Tasten werden uebersprungen.</summary>
    public void StepSlot(int direction)
    {
        lock (sync)
        {
            for (int i = 1; i <= slots.Length; i++)
            {
                int candidate = ((slotIndex + direction * i) % slots.Length + slots.Length) % slots.Length;
                if (slots[candidate].Page == null) continue;
                slotIndex = candidate;
                break;
            }
        }
        wake.Set();
        OnStateChanged();
    }

    public void SetDisplayOn(bool on)
    {
        lock (sync) displayOn = on;
        Log.Info(on ? "Display eingeschaltet." : "Display ausgeschaltet.");
        wake.Set();
        OnStateChanged();
    }

    private Slot[] BuildSlots(AppConfig source) =>
        AppConfig.SlotButtons
            .Select((label, i) =>
            {
                SlotConfig slotConfig = source.Slot(label);
                return new Slot(label, SlotIcpButtons[i], slotConfig, CreatePage(slotConfig));
            })
            .ToArray();

    /// <summary>
    /// Sendet die Poti-Stellungen an das ICP: SYM = DED-Beleuchtung, BRT = Tasten-LEDs, CONT = Kontrast.
    /// </summary>
    private void ApplyLights()
    {
        IcpInputSnapshot state = input.Current;
        if (ReferenceEquals(state, IcpInputSnapshot.Empty)) return;
        LightConfig lights;
        lock (sync) lights = config.Lights;

        int display = Math.Max(Math.Clamp(lights.DisplayMinPercent, 0, 100), state.Poti(IcpPoti.Sym));
        bool ok = SendLight(lights.DisplayChannel, display);
        ok &= SendLight(lights.KeysChannel, state.Poti(IcpPoti.Brt));
        ok &= SendLight(lights.ContrastChannel, state.Poti(IcpPoti.Cont));
        if (ok) lightsPending = false;
    }

    private bool SendLight(int channel, int percent)
    {
        if (channel is < 0 or > 255) return true;
        byte value = (byte)Math.Round(Math.Clamp(percent, 0, 100) * 2.55);
        lock (sentLights)
        {
            if (sentLights.TryGetValue(channel, out byte last) && last == value) return true;
            if (!device.WriteLight((byte)channel, value)) return false;
            sentLights[channel] = value;
        }
        Log.Info($"Licht Kanal {channel} = {percent}%");
        return true;
    }

    private void HandleButton(IcpButton button)
    {
        Log.Info($"Taste {IcpLayout.Label(button)}");
        // Ist das Display aus, weckt jede Taste es nur auf.
        if (!DisplayOn)
        {
            SetDisplayOn(true);
            return;
        }

        int slotButton = Array.IndexOf(SlotIcpButtons, button);
        if (slotButton >= 0)
        {
            Slot target = Slots[slotButton];
            if (target.Page == null) ShowStatus(L.DedKeyNotAssigned(target.Label));
            else SelectSlot(slotButton);
            return;
        }

        // Zuerst darf die angezeigte Seite die Taste nutzen (z. B. RSS: Dobber, ENTR).
        Page? shown;
        lock (sync) shown = overlayPage ?? slots[slotIndex].Page;
        if (shown?.HandleButton(button) == true)
        {
            if (shown is IcpPage) SendTestLight(); // neuer Testkanal bekommt sofort den RET-Wert
            wake.Set();
            return;
        }

        // Der DCS-Schalter (UP, DOWN, SEQ, RTN) hat bewusst keine Funktion.
        switch (button)
        {
            case IcpButton.DobberDown:
                StepSlot(+1);
                break;
            case IcpButton.DobberUp:
                StepSlot(-1);
                break;
            case IcpButton.WarnReset:
                SetDisplayOn(false);
                break;
            default:
                if (IcpLayout.Digit(button) is int digit) RunAction(digit);
                else wake.Set();
                break;
        }
    }

    private void RunAction(int digit)
    {
        ActionConfig? action;
        lock (sync) config.Actions.TryGetValue(digit.ToString(), out action);
        if (action == null || string.IsNullOrWhiteSpace(action.Command))
        {
            ShowStatus(L.DedNoAction(digit));
            return;
        }

        string name = string.IsNullOrWhiteSpace(action.Name) ? action.Command : action.Name;
        ShowStatus($"{digit}: {DedText.Normalize(name)}");
        Task.Run(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = action.Command,
                    Arguments = action.Arguments,
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = true,
                })?.Dispose();
                Log.Info($"Aktion {digit} gestartet: {action.Command} {action.Arguments}");
            }
            catch (Exception ex)
            {
                Log.Error($"Aktion {digit} fehlgeschlagen", ex);
                ShowStatus($"{digit}: {L.DedError}");
            }
        });
    }

    /// <summary>Blendet fuer einige Sekunden eine invertierte Meldung in der untersten Zeile ein.</summary>
    public void ShowStatus(string message)
    {
        lock (sync)
        {
            statusMessage = message;
            statusUntil = DateTime.Now + StatusDuration;
        }
        wake.Set();
    }

    private void RenderLoop()
    {
        var screen = new DedScreen();
        byte[]? lastFrame = null;
        DateTime lastWrite = DateTime.MinValue;
        bool wasConnected = false;

        while (!cts.IsCancellationRequested)
        {
            try
            {
                bool connected = device.IsConnected;
                if (connected && !wasConnected) lastFrame = null; // nach (Wieder-)Verbinden sofort neu zeichnen
                wasConnected = connected;

                if (connected)
                {
                    BuildScreen(screen);
                    byte[] frame = screen.Render(DedFont.Instance);
                    if (lastFrame == null || !frame.AsSpan().SequenceEqual(lastFrame) || DateTime.Now - lastWrite > KeepAliveInterval)
                    {
                        if (device.WriteFrame(frame))
                        {
                            if (lastFrame == null || SlotIndex != loggedSlot || config.LogDisplay) LogScreen(screen);
                            lastFrame = frame;
                            lastWrite = DateTime.Now;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Fehler beim Zeichnen", ex);
            }
            WaitHandle.WaitAny([wake, cts.Token.WaitHandle], config.RefreshMs);
        }
    }

    private int loggedSlot = -1;

    /// <summary>Displayinhalt ins Log schreiben (beim Verbinden und bei Seitenwechsel) - zur Fehlersuche ohne Blick aufs Geraet.</summary>
    private void LogScreen(DedScreen screen)
    {
        loggedSlot = SlotIndex;
        var lines = Enumerable.Range(0, DedScreen.Rows).Select(r => "  |" + screen.GetLine(r) + "|");
        Log.Info("Display:" + Environment.NewLine + string.Join(Environment.NewLine, lines));
    }

    private void BuildScreen(DedScreen screen)
    {
        screen.Clear();
        Slot slot;
        Page? overlay;
        string? status;
        lock (sync)
        {
            if (!displayOn) return;
            slot = slots[slotIndex];
            overlay = overlayPage;
            status = DateTime.Now < statusUntil ? statusMessage : null;
        }

        var context = new PageContext(sensors.Current, input.Current, device.IsConnected, overlay != null ? "TEST" : slot.Label);
        Page? page = overlay ?? slot.Page;
        if (page != null) page.Render(screen, context);
        else RenderEmpty(screen, slot.Label);
        if (status != null) screen.WriteLine(DedScreen.Rows - 1, DedFont.Instance.Sanitize(status), invert: true);
    }

    private static void RenderEmpty(DedScreen screen, string slotLabel)
    {
        screen.WriteCentered(1, L.DedKeyLabel(slotLabel));
        screen.WriteCentered(2, L.DedNotAssigned);
    }

    private void OnStateChanged()
    {
        try { StateChanged?.Invoke(); }
        catch (Exception ex) { Log.Error("Fehler im StateChanged-Handler", ex); }
    }

    public void Dispose()
    {
        cts.Cancel();
        renderThread?.Join(TimeSpan.FromSeconds(2));
        // Display beim Beenden leeren, damit keine veralteten Werte stehen bleiben.
        if (device.IsConnected) device.WriteFrame(new DedScreen().Render(DedFont.Instance));
        device.Dispose();
        sensors.Dispose();
        Rss.Dispose();
        wake.Dispose();
        cts.Dispose();
    }
}
