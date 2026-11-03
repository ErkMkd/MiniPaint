namespace MiniPaint.Core.ColorMode;

/// <summary>Amiga ECS (12-bit / 4096) and AGA (24-bit) channel helpers + Extra Half-Brite.</summary>
public static class AmigaColorSpace
{
    public static byte SnapChannel(byte value, int bitsPerChannel)
    {
        if (bitsPerChannel >= 8) return value;
        if (bitsPerChannel <= 0) return 0;
        int levels = (1 << bitsPerChannel) - 1;
        int v = (value * levels + 127) / 255;
        return (byte)((v * 255) / levels);
    }

    public static PaletteRgb SnapColor(byte r, byte g, byte b, int bitsPerChannel) =>
        new(SnapChannel(r, bitsPerChannel), SnapChannel(g, bitsPerChannel), SnapChannel(b, bitsPerChannel));

    public static PaletteRgb SnapColor(PaletteRgb c, int bitsPerChannel) =>
        SnapColor(c.R, c.G, c.B, bitsPerChannel);

    public static PaletteRgb HalfBright(PaletteRgb c) =>
        new((byte)(c.R / 2), (byte)(c.G / 2), (byte)(c.B / 2));

    /// <summary>
    /// Expand a unique palette into Extra Half-Brite display colors:
    /// indices 0..n-1 are base, n..2n-1 are half intensity of base.
    /// </summary>
    public static PaletteRgb[] ExpandExtraHalfBrite(IReadOnlyList<PaletteRgb> baseColors)
    {
        ArgumentNullException.ThrowIfNull(baseColors);
        int n = baseColors.Count;
        var result = new PaletteRgb[n * 2];
        for (int i = 0; i < n; i++)
        {
            result[i] = baseColors[i];
            result[i + n] = HalfBright(baseColors[i]);
        }
        return result;
    }
}
