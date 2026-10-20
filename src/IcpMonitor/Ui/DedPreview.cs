using IcpMonitor.Device;
using IcpMonitor.Display;

namespace IcpMonitor.Ui;

/// <summary>
/// Zeigt einen <see cref="DedScreen"/> pixelgenau so, wie er an das ICP gesendet wird
/// (es wird derselbe Pixelpuffer ausgewertet).
/// </summary>
internal sealed class DedPreview : Control
{
    private const int PixelScale = 3;
    private const int Inset = 10;

    private static readonly Color Background = Color.FromArgb(6, 14, 6);
    private static readonly Color PixelOn = Color.FromArgb(110, 255, 110);
    private static readonly Color PixelOff = Color.FromArgb(14, 28, 14);

    private byte[]? frame;

    public DedPreview()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Size = new Size(IcpProtocol.DisplayWidth * PixelScale + 2 * Inset, IcpProtocol.DisplayHeight * PixelScale + 2 * Inset);
        BackColor = Background;
    }

    public void ShowScreen(DedScreen screen)
    {
        byte[] next = screen.Render(DedFont.Instance);
        if (frame != null && next.AsSpan().SequenceEqual(frame)) return;
        frame = next;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Background);
        if (frame == null) return;

        // Die Pixel leicht kleiner als das Raster zeichnen - sieht aus wie das echte Punktmatrix-Display.
        int dot = PixelScale - 1;
        using var on = new SolidBrush(PixelOn);
        using var off = new SolidBrush(PixelOff);
        for (int y = 0; y < IcpProtocol.DisplayHeight; y++)
        {
            int rowOffset = IcpProtocol.RowOffset(y);
            for (int x = 0; x < IcpProtocol.DisplayWidth; x++)
            {
                bool lit = (frame[rowOffset + x / 8] & (1 << (x % 8))) != 0;
                g.FillRectangle(lit ? on : off, Inset + x * PixelScale, Inset + y * PixelScale, dot, dot);
            }
        }
    }
}
