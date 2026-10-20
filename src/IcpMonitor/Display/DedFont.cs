using System.Text;

namespace IcpMonitor.Display;

/// <summary>
/// Pixelschrift des F-16-DED (8x13 Pixel je Zeichen), aus ded_font.bmp von DedSharp (MIT, Byron Roosa).
/// Jede Glyphenzeile ist ein Byte, Bit 0 = linkes Pixel - genau das Format des Display-Speichers.
/// </summary>
internal sealed class DedFont
{
    public const int GlyphWidth = 8;
    public const int GlyphHeight = 13;

    // Reihenfolge der Zeichen im Bitmap. 0x01 = Pfeil hoch/runter, 0x02 = Stern, 0x7F = Platzhalter.
    private const string Charset = "ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890\x01()<>[]+-\x02/=^|~\x7f.,!?:;&_'\"%#@{} \x7f\x7f\x7f\x7f";

    private readonly Dictionary<char, byte[]> glyphs = new();
    private readonly byte[] fallback;

    public static DedFont Instance { get; } = new();

    private DedFont()
    {
        using Stream stream = typeof(DedFont).Assembly.GetManifestResourceStream("IcpMonitor.ded_font.bmp")
            ?? throw new InvalidOperationException("Ressource ded_font.bmp fehlt.");
        using var bitmap = new Bitmap(stream);

        for (int index = 0; index < Charset.Length; index++)
        {
            char c = Charset[index];
            byte[] rows = ReadGlyph(bitmap, index);
            if (c == '\x7f') continue;
            glyphs.TryAdd(c, rows);
            if (c == '\x02') glyphs.TryAdd('*', rows);
        }
        fallback = ReadGlyph(bitmap, Charset.Length - 1);
    }

    /// <summary>13 Zeilenbytes des Zeichens; Kleinbuchstaben werden zu Grossbuchstaben.</summary>
    public byte[] Glyph(char c) =>
        glyphs.TryGetValue(char.ToUpperInvariant(c), out byte[]? rows) ? rows : fallback;

    /// <summary>Ersetzt Zeichen, die die Schrift nicht kennt, durch '?'.</summary>
    public string Sanitize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text.ToUpperInvariant()) sb.Append(glyphs.ContainsKey(c) ? c : '?');
        return sb.ToString();
    }

    private static byte[] ReadGlyph(Bitmap bitmap, int index)
    {
        var rows = new byte[GlyphHeight];
        for (int y = 0; y < GlyphHeight; y++)
        {
            for (int x = 0; x < GlyphWidth; x++)
            {
                if (bitmap.GetPixel(index * GlyphWidth + x, y).GetBrightness() > 0) rows[y] |= (byte)(1 << x);
            }
        }
        return rows;
    }
}
