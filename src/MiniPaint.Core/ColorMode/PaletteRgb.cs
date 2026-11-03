namespace MiniPaint.Core.ColorMode;

public readonly struct PaletteRgb : IEquatable<PaletteRgb>
{
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    public PaletteRgb(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    public static PaletteRgb FromRgb(int r, int g, int b) =>
        new((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));

    public int DistanceSquared(byte r, byte g, byte b)
    {
        int dr = R - r;
        int dg = G - g;
        int db = B - b;
        return dr * dr + dg * dg + db * db;
    }

    public bool Equals(PaletteRgb other) => R == other.R && G == other.G && B == other.B;
    public override bool Equals(object? obj) => obj is PaletteRgb p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(R, G, B);
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}
