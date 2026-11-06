namespace MiniPaint.Core.Selection;

/// <summary>Color comparison shared by Magic Wand and Color Range.</summary>
internal static class ColorMetric
{
    /// <summary>RGB premultiplied by alpha, so all fully transparent pixels compare equal.</summary>
    public static (int r, int g, int b, int a) Premultiplied(byte r, byte g, byte b, byte a)
    {
        if (a == 255) return (r, g, b, 255);
        return ((r * a + 127) / 255, (g * a + 127) / 255, (b * a + 127) / 255, a);
    }

    public static (int r, int g, int b, int a) Premultiplied(ReadOnlySpan<byte> px, int offset) =>
        Premultiplied(px[offset], px[offset + 1], px[offset + 2], px[offset + 3]);

    /// <summary>Largest per-channel difference, 0–255.</summary>
    public static int MaxDifference((int r, int g, int b, int a) x, (int r, int g, int b, int a) y) =>
        Math.Max(Math.Max(Math.Abs(x.r - y.r), Math.Abs(x.g - y.g)),
                 Math.Max(Math.Abs(x.b - y.b), Math.Abs(x.a - y.a)));
}
