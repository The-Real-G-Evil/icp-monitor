using System.Text;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace IcpMonitor.Sensors;

/// <summary>
/// Liest die Hardware-Sensoren per LibreHardwareMonitorLib in einem Hintergrund-Thread
/// und stellt sie als <see cref="SystemSnapshot"/> bereit.
/// </summary>
internal sealed partial class SensorService : IDisposable
{
    [GeneratedRegex(@"-\d{4}$")]
    private static partial Regex FilterAdapterName();

    private readonly int intervalMs;
    private readonly CancellationTokenSource cts = new();
    private readonly bool isAdmin = Program.IsAdmin();
    private Thread? thread;
    private Computer? computer;

    public SensorService(int intervalMs) => this.intervalMs = intervalMs;

    public SystemSnapshot Current { get; private set; } = SystemSnapshot.Empty;

    public static string DumpPath => AppPaths.SensorDumpFile;

    public void Start()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "Sensoren", Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }

    private void Run()
    {
        try
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsNetworkEnabled = true,
                IsStorageEnabled = true,
            };
            computer.Open();
            Log.Info($"Sensoren initialisiert (Administrator: {isAdmin}).");
        }
        catch (Exception ex)
        {
            Log.Error("LibreHardwareMonitor konnte nicht gestartet werden", ex);
            return;
        }

        bool dumped = false;
        while (!cts.IsCancellationRequested)
        {
            try
            {
                foreach (IHardware hardware in computer.Hardware) UpdateRecursive(hardware);
                Current = BuildSnapshot(computer);
                if (!dumped)
                {
                    WriteDump(computer);
                    dumped = true;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Fehler beim Lesen der Sensoren", ex);
            }
            cts.Token.WaitHandle.WaitOne(intervalMs);
        }
    }

    private static void UpdateRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (IHardware sub in hardware.SubHardware) UpdateRecursive(sub);
    }

    private SystemSnapshot BuildSnapshot(Computer pc)
    {
        IHardware? cpu = pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        // Bei mehreren GPUs (z. B. Ryzen-iGPU + Grafikkarte) die mit dem meisten Grafikspeicher nehmen.
        IHardware? gpu = pc.Hardware
            .Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            .OrderByDescending(h => Find(h, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total") ?? 0)
            .FirstOrDefault();
        IHardware? ram = pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && !h.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase));
        IHardware? virt = pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && h.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase));
        // Windows meldet jeden Adapter zusaetzlich ueber seine Filtertreiber ("Ethernet-QoS Packet Scheduler-0000" usw.);
        // die wuerden den Durchsatz mehrfach zaehlen.
        var networks = pc.Hardware
            .Where(h => h.HardwareType == HardwareType.Network && !FilterAdapterName().IsMatch(h.Name) && !h.Name.Contains("Kerneldebug", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new SystemSnapshot
        {
            Timestamp = DateTime.Now,
            IsAdmin = isAdmin,

            CpuName = cpu?.Name ?? "",
            CpuLoad = Find(cpu, SensorType.Load, "CPU Total"),
            CpuMaxCoreLoad = Find(cpu, SensorType.Load, "CPU Core Max")
                ?? Max(cpu, SensorType.Load, s => s.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase)),
            CpuTemp = Find(cpu, SensorType.Temperature, "Core (Tctl/Tdie)", "CPU Package", "Core (Tctl)", "Core Average", "Core Max")
                ?? Max(cpu, SensorType.Temperature),
            CpuPower = Find(cpu, SensorType.Power, "Package", "CPU Package"),
            CpuClockMhz = Find(cpu, SensorType.Clock, "Cores (Average)")
                ?? Average(cpu, SensorType.Clock, s => s.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase) && !s.Name.Contains("Effective")),

            GpuName = gpu?.Name ?? "",
            GpuLoad = Find(gpu, SensorType.Load, "GPU Core", "D3D 3D"),
            GpuTemp = Find(gpu, SensorType.Temperature, "GPU Core", "GPU VR SoC"),
            GpuHotSpot = Find(gpu, SensorType.Temperature, "GPU Hot Spot"),
            GpuMemTemp = Find(gpu, SensorType.Temperature, "GPU Memory", "GPU Memory Junction"),
            GpuPower = Find(gpu, SensorType.Power, "GPU Package", "GPU Power", "GPU Core") ?? Max(gpu, SensorType.Power),
            GpuClockMhz = Find(gpu, SensorType.Clock, "GPU Core"),
            GpuFanRpm = Find(gpu, SensorType.Fan, "GPU Fan", "GPU Fan 1"),
            GpuMemUsedMb = Find(gpu, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used"),
            GpuMemTotalMb = Find(gpu, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total"),

            RamUsedGb = Find(ram, SensorType.Data, "Memory Used"),
            RamAvailableGb = Find(ram, SensorType.Data, "Memory Available"),
            RamLoad = Find(ram, SensorType.Load, "Memory"),

            VirtUsedGb = Find(virt, SensorType.Data, "Memory Used"),
            VirtAvailableGb = Find(virt, SensorType.Data, "Memory Available"),
            VirtLoad = Find(virt, SensorType.Load, "Memory"),

            NetDownBps = Sum(networks, SensorType.Throughput, "Download Speed"),
            NetUpBps = Sum(networks, SensorType.Throughput, "Upload Speed"),
            NetDownTotalGb = Sum(networks, SensorType.Data, "Data Downloaded"),
            NetUpTotalGb = Sum(networks, SensorType.Data, "Data Uploaded"),
            NetAdapter = networks.OrderByDescending(n => Find(n, SensorType.Data, "Data Downloaded") ?? 0).FirstOrDefault()?.Name ?? "",

            FanMaxRpm = pc.Hardware.SelectMany(AllSensors).Where(s => s.SensorType == SensorType.Fan && s.Value > 0).Max(s => s.Value),

            Drives = ReadDrives(),
            Disks = pc.Hardware
                .Where(h => h.HardwareType == HardwareType.Storage)
                .Select(h => new DiskSnapshot(
                    h.Name,
                    Find(h, SensorType.Temperature, "Temperature", "Composite Temperature") ?? Max(h, SensorType.Temperature),
                    Find(h, SensorType.Load, "Total Activity"),
                    Find(h, SensorType.Throughput, "Read Rate"),
                    Find(h, SensorType.Throughput, "Write Rate")))
                .ToList(),
        };
    }

    private static List<DriveSnapshot> ReadDrives()
    {
        var drives = new List<DriveSnapshot>();
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                drives.Add(new DriveSnapshot(drive.Name[..1], drive.VolumeLabel, drive.TotalSize / 1e9, drive.AvailableFreeSpace / 1e9));
            }
            catch (IOException)
            {
                // Laufwerk gerade nicht lesbar (z. B. BitLocker gesperrt) - weglassen.
            }
        }
        return drives;
    }

    private static IEnumerable<ISensor> AllSensors(IHardware hardware) =>
        hardware.Sensors.Concat(hardware.SubHardware.SelectMany(AllSensors));

    /// <summary>Wert des ersten Sensors, dessen Name einem der Kandidaten entspricht (in dieser Reihenfolge).</summary>
    private static float? Find(IHardware? hardware, SensorType type, params string[] names)
    {
        if (hardware == null) return null;
        var sensors = AllSensors(hardware).Where(s => s.SensorType == type && IsValid(s)).ToList();
        foreach (string name in names)
        {
            ISensor? match = sensors.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match.Value;
        }
        return null;
    }

    /// <summary>
    /// Ohne Treiberzugriff (kein Administrator) liefert LibreHardwareMonitor fuer Temperatur, Takt
    /// und CPU-Leistung 0 statt "kein Wert" - das ist dann als fehlend zu behandeln.
    /// </summary>
    private static bool IsValid(ISensor sensor) =>
        sensor.Value.HasValue &&
        (sensor.Value > 0 || sensor.SensorType is not (SensorType.Temperature or SensorType.Clock or SensorType.Power));

    private static float? Max(IHardware? hardware, SensorType type, Func<ISensor, bool>? filter = null) =>
        hardware == null ? null : AllSensors(hardware).Where(s => s.SensorType == type && s.Value > 0 && (filter == null || filter(s))).Max(s => s.Value);

    private static float? Average(IHardware? hardware, SensorType type, Func<ISensor, bool> filter)
    {
        if (hardware == null) return null;
        var values = AllSensors(hardware).Where(s => s.SensorType == type && filter(s) && IsValid(s)).Select(s => s.Value!.Value).ToList();
        return values.Count > 0 ? values.Average() : null;
    }

    private static float? Sum(IEnumerable<IHardware> hardware, SensorType type, string name)
    {
        var values = hardware.SelectMany(AllSensors)
            .Where(s => s.SensorType == type && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
            .Select(s => s.Value!.Value)
            .ToList();
        return values.Count > 0 ? values.Sum() : null;
    }

    /// <summary>Schreibt alle gefundenen Sensoren in sensors.txt - hilfreich, um neue Seiten zu planen.</summary>
    private void WriteDump(Computer pc)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Sensorliste vom {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Administrator: {isAdmin}");
            foreach (IHardware hardware in pc.Hardware) DumpHardware(sb, hardware, 0);
            File.WriteAllText(DumpPath, sb.ToString());
        }
        catch (Exception ex)
        {
            Log.Warn($"sensors.txt konnte nicht geschrieben werden: {ex.Message}");
        }
    }

    private static void DumpHardware(StringBuilder sb, IHardware hardware, int depth)
    {
        string indent = new(' ', depth * 2);
        sb.AppendLine($"{indent}[{hardware.HardwareType}] {hardware.Name}");
        foreach (ISensor sensor in hardware.Sensors.OrderBy(s => s.SensorType).ThenBy(s => s.Index))
        {
            sb.AppendLine($"{indent}  {sensor.SensorType,-12} {sensor.Name,-36} {sensor.Value?.ToString("0.##") ?? "-"}");
        }
        foreach (IHardware sub in hardware.SubHardware) DumpHardware(sb, sub, depth + 1);
    }

    public void Dispose()
    {
        cts.Cancel();
        thread?.Join(TimeSpan.FromSeconds(3));
        try { computer?.Close(); } catch { }
        cts.Dispose();
    }
}
