using System.Globalization;
using System.Text;

namespace IcpMonitor.Display;

/// <summary>Bereitet beliebigen Text fuer die DED-Schrift auf (nur Grossbuchstaben, kein Unicode).</summary>
internal static class DedText
{
    private static readonly (string From, string To)[] Replacements =
    [
        ("Ä", "AE"), ("Ö", "OE"), ("Ü", "UE"), ("ä", "AE"), ("ö", "OE"), ("ü", "UE"), ("ß", "SS"),
        ("„", "\""), ("“", "\""), ("”", "\""), ("«", "\""), ("»", "\""),
        ("‚", "'"), ("‘", "'"), ("’", "'"), ("´", "'"), ("`", "'"),
        ("–", "-"), ("—", "-"), ("…", "..."), ("€", "EUR"), ("°", ""), ("•", "-"), ("·", "-"),
    ];

    /// <summary>Umlaute ausschreiben, Akzente entfernen, Leerraum zusammenfassen, Unbekanntes durch '?' ersetzen.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        foreach ((string from, string to) in Replacements) text = text.Replace(from, to);

        var sb = new StringBuilder(text.Length);
        bool lastWasSpace = false;
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && sb.Length > 0) sb.Append(' ');
                lastWasSpace = true;
                continue;
            }
            sb.Append(c);
            lastWasSpace = false;
        }
        return DedFont.Instance.Sanitize(sb.ToString().TrimEnd());
    }

    /// <summary>Kuerzt auf <paramref name="width"/> Zeichen.</summary>
    public static string Fit(string text, int width) => text.Length <= width ? text : text[..width];

    /// <summary>
    /// Bricht an Wortgrenzen um. Passt der Text nicht in <paramref name="maxLines"/> Zeilen,
    /// endet die letzte Zeile mit "..".
    /// </summary>
    public static List<string> Wrap(string text, int width, int maxLines)
    {
        var lines = new List<string>();
        var words = new Queue<string>(text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var line = new StringBuilder();

        while (words.Count > 0)
        {
            string word = words.Peek();
            if (word.Length > width)
            {
                // Ueberlange Woerter (URLs o. ae.) hart trennen.
                int space = width - line.Length - (line.Length > 0 ? 1 : 0);
                if (space <= 0) { lines.Add(line.ToString()); line.Clear(); continue; }
                words.Dequeue();
                if (line.Length > 0) line.Append(' ');
                line.Append(word[..space]);
                lines.Add(line.ToString());
                line.Clear();
                words = new Queue<string>(words.Prepend(word[space..]));
                continue;
            }
            int needed = line.Length == 0 ? word.Length : line.Length + 1 + word.Length;
            if (needed > width)
            {
                lines.Add(line.ToString());
                line.Clear();
                continue;
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(words.Dequeue());
        }
        if (line.Length > 0) lines.Add(line.ToString());

        if (lines.Count > maxLines)
        {
            lines.RemoveRange(maxLines, lines.Count - maxLines);
            string last = lines[^1];
            lines[^1] = (last.Length > width - 2 ? last[..(width - 2)] : last) + "..";
        }
        return lines;
    }
}
