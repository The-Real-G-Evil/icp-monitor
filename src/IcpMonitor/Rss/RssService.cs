using System.Collections.Concurrent;

namespace IcpMonitor.Rss;

/// <summary>Zustand eines Feeds aus Sicht der Seiten.</summary>
internal sealed record FeedStatus(string Url, string Title, IReadOnlyList<RssItem> Items, string? Error, bool Loading);

/// <summary>
/// Laedt Feeds im Hintergrund und haelt sie zwischengespeichert. Seiten fragen nur ab;
/// veraltete oder neue Feeds werden dabei automatisch (neu) geladen.
/// </summary>
internal sealed class RssService : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(1);

    private readonly HttpClient http;
    private readonly ConcurrentDictionary<string, FeedEntry> feeds = new(StringComparer.OrdinalIgnoreCase);

    public RssService()
    {
        http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(20),
            // Schutz vor riesigen Antworten: Feeds sind normalerweise wenige hundert KB.
            MaxResponseContentBufferSize = 10 * 1024 * 1024,
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("IcpMonitor/0.2 (+RSS)");
    }

    /// <summary>Aktueller Stand eines Feeds; stoesst bei Bedarf das Laden an.</summary>
    public FeedStatus Get(string url)
    {
        FeedEntry entry = feeds.GetOrAdd(url, u => new FeedEntry(u));
        lock (entry)
        {
            TimeSpan wait = entry.Error == null ? RefreshInterval : RetryInterval;
            if (!entry.Loading && DateTime.Now - entry.LastAttempt > wait)
            {
                entry.Loading = true;
                entry.LastAttempt = DateTime.Now;
                _ = LoadAsync(entry);
            }
            return new FeedStatus(url, entry.Title, entry.Items, entry.Error, entry.Loading);
        }
    }

    private async Task LoadAsync(FeedEntry entry)
    {
        ParsedFeed? parsed = null;
        string? error = null;
        try
        {
            // Als Bytes laden: Manche Server melden im HTTP-Header eine falsche Zeichenkodierung,
            // die XML-Deklaration im Dokument selbst stimmt dagegen.
            byte[] data = await http.GetByteArrayAsync(entry.Url).ConfigureAwait(false);
            parsed = FeedParser.Parse(data, new Uri(entry.Url).Host);
            Log.Info($"RSS geladen: {entry.Url} - \"{parsed.Title}\", {parsed.Items.Count} Meldungen, neueste: " +
                     (parsed.Items.Count > 0 ? $"\"{parsed.Items[0].Title}\" ({parsed.Items[0].Published:g})" : "-"));
        }
        catch (Exception ex)
        {
            error = ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException
                ? ex.Message
                : "Feed nicht lesbar: " + ex.Message;
            Log.Warn($"RSS-Fehler bei {entry.Url}: {ex.Message}");
        }

        lock (entry)
        {
            entry.Loading = false;
            entry.Error = error;
            if (parsed != null)
            {
                entry.Title = parsed.Title;
                entry.Items = parsed.Items;
            }
        }
    }

    public void Dispose() => http.Dispose();

    private sealed class FeedEntry(string url)
    {
        public string Url { get; } = url;
        public string Title { get; set; } = "";
        public IReadOnlyList<RssItem> Items { get; set; } = [];
        public string? Error { get; set; }
        public bool Loading { get; set; }
        public DateTime LastAttempt { get; set; } = DateTime.MinValue;
    }
}
