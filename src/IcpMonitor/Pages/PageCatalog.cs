using IcpMonitor.Rss;

namespace IcpMonitor.Pages;

/// <summary>Ein waehlbarer Seitentyp. Name und Beschreibung folgen der eingestellten Sprache.</summary>
internal sealed record PageType(string Id, Func<string> Name, Func<string> Description, Func<SlotConfig, RssService, Page?> Create)
{
    /// <summary>Fuer ComboBox.DisplayMember.</summary>
    public string DisplayName => Name();
}

/// <summary>Alle Seitentypen, die einer ICP-Taste zugewiesen werden koennen.</summary>
internal static class PageCatalog
{
    public const string None = "NONE";

    public static readonly PageType[] Types =
    [
        new(None, () => L.PageNone, () => L.PageNoneDesc, (_, _) => null),
        new("SYSTEM", () => L.PageSystem, () => L.PageSystemDesc, (_, _) => new SystemPage()),
        new("CPU", () => L.PageCpu, () => L.PageCpuDesc, (_, _) => new CpuPage()),
        new("GPU", () => L.PageGpu, () => L.PageGpuDesc, (_, _) => new GpuPage()),
        new("MEMORY", () => L.PageMemory, () => L.PageMemoryDesc, (_, _) => new MemoryPage()),
        new("STORAGE", () => L.PageStorage, () => L.PageStorageDesc, (_, _) => new StoragePage()),
        new("NETWORK", () => L.PageNetwork, () => L.PageNetworkDesc, (_, _) => new NetworkPage()),
        new("CLOCK", () => L.PageClock, () => L.PageClockDesc, (_, _) => new ClockPage()),
        new("RSS", () => L.PageRss, () => L.PageRssDesc, (slot, rss) => new RssPage(rss, slot.Rss ?? new RssOptions())),
    ];
    // Die ICP-Testseite liegt auf keiner Taste, sie wird ueber Tray-Menue/Einstellungen eingeblendet
    // (MonitorApp.ToggleTestPage).

    public static PageType Get(string? id) =>
        Array.Find(Types, t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? Types[0];
}
