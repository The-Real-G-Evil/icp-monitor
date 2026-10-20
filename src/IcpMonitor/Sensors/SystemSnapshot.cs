namespace IcpMonitor.Sensors;

/// <summary>Momentaufnahme der Systemwerte. null = Sensor nicht vorhanden oder (noch) nicht lesbar.</summary>
internal sealed record SystemSnapshot
{
    public static readonly SystemSnapshot Empty = new();

    public DateTime Timestamp { get; init; }

    public string CpuName { get; init; } = "";
    public float? CpuLoad { get; init; }
    public float? CpuMaxCoreLoad { get; init; }
    public float? CpuTemp { get; init; }
    public float? CpuPower { get; init; }
    public float? CpuClockMhz { get; init; }

    public string GpuName { get; init; } = "";
    public float? GpuLoad { get; init; }
    public float? GpuTemp { get; init; }
    public float? GpuHotSpot { get; init; }
    public float? GpuMemTemp { get; init; }
    public float? GpuPower { get; init; }
    public float? GpuClockMhz { get; init; }
    public float? GpuFanRpm { get; init; }
    public float? GpuMemUsedMb { get; init; }
    public float? GpuMemTotalMb { get; init; }

    public float? RamUsedGb { get; init; }
    public float? RamAvailableGb { get; init; }
    public float? RamLoad { get; init; }

    public float? VirtUsedGb { get; init; }
    public float? VirtAvailableGb { get; init; }
    public float? VirtLoad { get; init; }

    /// <summary>Summe aller Netzwerkadapter in Byte/s.</summary>
    public float? NetDownBps { get; init; }
    public float? NetUpBps { get; init; }

    /// <summary>Seit Adapterstart uebertragene Datenmenge in GB.</summary>
    public float? NetDownTotalGb { get; init; }
    public float? NetUpTotalGb { get; init; }

    /// <summary>Adapter mit dem meisten Datenverkehr.</summary>
    public string NetAdapter { get; init; } = "";

    public float? FanMaxRpm { get; init; }

    /// <summary>Lokale Festplatten-Laufwerke (C:, D:, ...).</summary>
    public IReadOnlyList<DriveSnapshot> Drives { get; init; } = [];

    /// <summary>Physische Datentraeger mit Temperatur und Aktivitaet (LibreHardwareMonitor).</summary>
    public IReadOnlyList<DiskSnapshot> Disks { get; init; } = [];

    public bool IsAdmin { get; init; }
}

internal sealed record DriveSnapshot(string Letter, string Label, double TotalGb, double FreeGb)
{
    public double UsedPercent => TotalGb > 0 ? (TotalGb - FreeGb) / TotalGb * 100 : 0;
}

/// <param name="ActivityPercent">Anteil der Zeit, in der der Datentraeger beschaeftigt ist.</param>
internal sealed record DiskSnapshot(string Name, float? Temp, float? ActivityPercent, float? ReadBps, float? WriteBps);
