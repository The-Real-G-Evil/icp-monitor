using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace IcpMonitor.Rss;

internal sealed record RssItem(string Source, string Title, DateTimeOffset? Published, string? Link);

internal sealed record ParsedFeed(string Title, IReadOnlyList<RssItem> Items);

/// <summary>
/// Liest RSS 2.0, RSS 1.0 (RDF) und Atom. Bewusst tolerant: Elemente werden nur ueber ihren
/// lokalen Namen gesucht, kaputte Datumsangaben ergeben einfach kein Datum.
/// </summary>
internal static partial class FeedParser
{
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s([+-]\d{2})(\d{2})$")]
    private static partial Regex CompactOffset();

    private static readonly Dictionary<string, string> ZoneNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GMT"] = "+00:00", ["UT"] = "+00:00", ["UTC"] = "+00:00", ["Z"] = "+00:00",
        ["CET"] = "+01:00", ["CEST"] = "+02:00", ["MEZ"] = "+01:00", ["MESZ"] = "+02:00",
        ["EST"] = "-05:00", ["EDT"] = "-04:00", ["CST"] = "-06:00", ["CDT"] = "-05:00",
        ["MST"] = "-07:00", ["MDT"] = "-06:00", ["PST"] = "-08:00", ["PDT"] = "-07:00",
    };

    public static ParsedFeed Parse(byte[] xml, string fallbackTitle)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(new MemoryStream(xml), settings);
        XElement root = XDocument.Load(reader).Root ?? throw new FormatException("Leeres XML-Dokument.");

        bool atom = root.Name.LocalName == "feed";
        XElement channel = atom ? root : Child(root, "channel") ?? root;
        string title = ShortTitle(Clean(Child(channel, "title")?.Value));
        if (title.Length == 0) title = fallbackTitle;

        // RSS 2.0: channel/item, RSS 1.0: rdf:RDF/item, Atom: feed/entry
        IEnumerable<XElement> entries = atom
            ? root.Elements().Where(e => e.Name.LocalName == "entry")
            : channel.Elements().Concat(root.Elements()).Where(e => e.Name.LocalName == "item").Distinct();

        var items = new List<RssItem>();
        foreach (XElement entry in entries)
        {
            string itemTitle = Clean(Child(entry, "title")?.Value);
            if (itemTitle.Length == 0) continue;
            string? link = atom ? AtomLink(entry) : Child(entry, "link")?.Value.Trim();
            string? date = (Child(entry, "pubDate") ?? Child(entry, "published") ?? Child(entry, "updated") ?? Child(entry, "date"))?.Value;
            items.Add(new RssItem(title, itemTitle, ParseDate(date), string.IsNullOrWhiteSpace(link) ? null : link));
        }
        return new ParsedFeed(title, items);
    }

    private static XElement? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    private static string? AtomLink(XElement entry)
    {
        var links = entry.Elements().Where(e => e.Name.LocalName == "link").ToList();
        XElement? link = links.FirstOrDefault(l => (string?)l.Attribute("rel") is null or "alternate") ?? links.FirstOrDefault();
        return (string?)link?.Attribute("href");
    }

    /// <summary>"tagesschau.de - die erste Adresse fuer ..." -> "tagesschau.de"</summary>
    private static string ShortTitle(string title)
    {
        foreach (string separator in new[] { " - ", " | ", " – ", ": " })
        {
            int cut = title.IndexOf(separator, StringComparison.Ordinal);
            if (cut > 0) title = title[..cut];
        }
        return title.Trim();
    }

    /// <summary>HTML-Tags und -Entities entfernen, Leerraum zusammenfassen.</summary>
    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        text = WebUtility.HtmlDecode(HtmlTag().Replace(text, " "));
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static DateTimeOffset? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        const DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal;
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, styles, out var result)) return result;

        // RFC 822 (RSS): "Tue, 20 Oct 2026 18:00:00 +0200" bzw. "... GMT"
        string normalized = CompactOffset().Replace(text, " $1:$2");
        int lastSpace = normalized.LastIndexOf(' ');
        if (lastSpace > 0 && ZoneNames.TryGetValue(normalized[(lastSpace + 1)..], out string? offset))
        {
            normalized = normalized[..lastSpace] + " " + offset;
        }
        return DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, styles, out result) ? result : null;
    }
}
