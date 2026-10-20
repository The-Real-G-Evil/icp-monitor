using IcpMonitor.Device;

namespace IcpMonitor.Display;

/// <summary>Textpuffer des DED: 5 Zeilen x 24 Zeichen, jedes Zeichen optional invertiert.</summary>
internal sealed class DedScreen
{
    public const int Columns = 24;
    public const int Rows = 5;

    private readonly char[,] chars = new char[Rows, Columns];
    private readonly bool[,] inverted = new bool[Rows, Columns];

    public DedScreen() => Clear();

    public void Clear()
    {
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Columns; c++)
            {
                chars[r, c] = ' ';
                inverted[r, c] = false;
            }
        }
    }

    /// <summary>Schreibt Text ab Spalte <paramref name="col"/>; alles jenseits von Spalte 24 wird abgeschnitten.</summary>
    public void Write(int row, int col, string text, bool invert = false)
    {
        if (row is < 0 or >= Rows) return;
        for (int i = 0; i < text.Length; i++)
        {
            int c = col + i;
            if (c is < 0 or >= Columns) continue;
            chars[row, c] = text[i];
            inverted[row, c] = invert;
        }
    }

    public void WriteRight(int row, string text, bool invert = false) => Write(row, Columns - text.Length, text, invert);

    public void WriteCentered(int row, string text, bool invert = false) => Write(row, (Columns - text.Length) / 2, text, invert);

    /// <summary>Ganze Zeile setzen, mit Leerzeichen auf 24 Zeichen aufgefuellt.</summary>
    public void WriteLine(int row, string text, bool invert = false) =>
        Write(row, 0, text.Length >= Columns ? text[..Columns] : text.PadRight(Columns), invert);

    public string GetLine(int row)
    {
        var line = new char[Columns];
        for (int c = 0; c < Columns; c++) line[c] = chars[row, c];
        return new string(line);
    }

    /// <summary>Erzeugt den Pixelpuffer fuer <see cref="IcpProtocol"/>.</summary>
    public byte[] Render(DedFont font)
    {
        // 24 Zeichen x 8 px = 192 px; die Spalten 192-199 bleiben dunkel.
        // Da jede Glyphe genau ein Byte breit ist, landet jede Glyphenzeile direkt in einem Pufferbyte.
        var buffer = new byte[IcpProtocol.PixelBufferLength];
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Columns; c++)
            {
                byte[] glyph = font.Glyph(chars[r, c]);
                bool invert = inverted[r, c];
                for (int gy = 0; gy < DedFont.GlyphHeight; gy++)
                {
                    int y = r * DedFont.GlyphHeight + gy;
                    buffer[IcpProtocol.RowOffset(y) + c] = invert ? (byte)~glyph[gy] : glyph[gy];
                }
            }
        }
        return buffer;
    }
}
