using System.Diagnostics;
using IcpMonitor.Device;
using IcpMonitor.Display;
using IcpMonitor.Rss;

namespace IcpMonitor.Pages;

/// <summary>
/// Schlagzeilen, eine pro Bildschirm, wechseln automatisch:
/// <code>
/// TAGESSCHAU.DE        3/40
/// BUNDESTAG BESCHLIESST
/// NEUES GESETZ ZUR
/// DIGITALISIERUNG
///                VOR 12 MIN
/// </code>
/// Dobber hoch/runter blaettert, ENTR oeffnet den Artikel im Browser.
/// </summary>
internal sealed class RssPage(RssService rss, RssOptions options) : Page
{
    private const int MaxItems = 60;

    private readonly object sync = new();
    private int index;
    private DateTime lastSwitch = DateTime.Now;
    private DateTime lastRender = DateTime.MinValue;
    private RssItem? current;

    public override void Render(DedScreen screen, PageContext context)
    {
        if (options.Feeds.Count == 0)
        {
            RenderHeader(screen, context, "RSS");
            screen.Write(1, 0, L.DedNoFeeds);
            screen.Write(3, 0, L.DedFeedsHint1);
            screen.Write(4, 0, L.DedFeedsHint2);
            return;
        }

        var statuses = options.Feeds.Select(rss.Get).ToList();
        var items = statuses.SelectMany(s => s.Items);
        // Mehrere Feeds: neueste Meldungen zuerst; ein Feed: Reihenfolge des Anbieters behalten.
        if (statuses.Count > 1) items = items.OrderByDescending(i => i.Published ?? DateTimeOffset.MinValue);
        List<RssItem> list = items.Take(MaxItems).ToList();

        if (list.Count == 0)
        {
            RenderHeader(screen, context, "RSS");
            FeedStatus? failed = statuses.FirstOrDefault(s => s.Error != null);
            if (statuses.Any(s => s.Loading) || failed == null)
            {
                screen.Write(2, 0, L.DedLoadingFeeds);
            }
            else
            {
                screen.Write(1, 0, L.DedError + ":");
                var lines = DedText.Wrap(DedText.Normalize(failed.Error), DedScreen.Columns, 3);
                for (int i = 0; i < lines.Count; i++) screen.Write(2 + i, 0, lines[i]);
            }
            return;
        }

        RssItem item;
        int number;
        lock (sync)
        {
            // War die Seite nicht sichtbar, die aktuelle Meldung erst noch die volle Zeit zeigen.
            if (DateTime.Now - lastRender > TimeSpan.FromSeconds(2)) lastSwitch = DateTime.Now;
            lastRender = DateTime.Now;
            if (DateTime.Now - lastSwitch > TimeSpan.FromSeconds(options.RotateSeconds))
            {
                index++;
                lastSwitch = DateTime.Now;
            }
            index = ((index % list.Count) + list.Count) % list.Count;
            item = list[index];
            number = index + 1;
            current = item;
        }

        string counter = $"{number}/{list.Count}";
        screen.Write(0, 0, DedText.Fit(DedText.Normalize(item.Source), DedScreen.Columns - counter.Length - 1));
        screen.WriteRight(0, counter);

        var titleLines = DedText.Wrap(DedText.Normalize(item.Title), DedScreen.Columns, 4);
        for (int i = 0; i < titleLines.Count; i++) screen.Write(1 + i, 0, titleLines[i]);
        if (titleLines.Count < 4 && item.Published.HasValue) screen.WriteRight(4, Age(item.Published.Value));
    }

    public override bool HandleButton(IcpButton button)
    {
        switch (button)
        {
            case IcpButton.DobberUp:
                Step(-1);
                return true;
            case IcpButton.DobberDown:
                Step(+1);
                return true;
            case IcpButton.Entr:
                OpenCurrent();
                return true;
            default:
                return false;
        }
    }

    private void Step(int delta)
    {
        lock (sync)
        {
            index += delta;
            lastSwitch = DateTime.Now;
        }
    }

    private void OpenCurrent()
    {
        string? link;
        lock (sync) link = current?.Link;
        if (link == null || !Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")) return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"Artikel konnte nicht geoeffnet werden: {ex.Message}");
        }
    }

    private static string Age(DateTimeOffset published)
    {
        TimeSpan age = DateTimeOffset.Now - published;
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age.TotalMinutes < 60) return L.DedMinutesAgo((int)age.TotalMinutes);
        if (age.TotalHours < 24) return L.DedHoursAgo((int)age.TotalHours);
        return published.ToLocalTime().ToString("g", L.Culture);
    }
}
