using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Filters;

/// <summary>
/// A non-destructive filter on a layer or a group: it keeps its settings and is applied each time
/// the image is composed, so it can be edited, switched off or removed at any time.
/// Filters are immutable values; editing one means replacing it.
/// </summary>
public abstract record LayerFilter(bool Enabled)
{
    /// <summary>Menu and panel name.</summary>
    public abstract string Name { get; }

    /// <summary>The settings at a glance, for the filters list.</summary>
    public abstract string Summary { get; }

    /// <summary>True when the settings leave every pixel unchanged.</summary>
    public abstract bool IsIdentity { get; }

    /// <summary>Change the colors of <paramref name="pixels"/> in place (alpha is kept).</summary>
    public abstract void Apply(PixelBuffer pixels);

    /// <summary>True when at least one filter of the list changes pixels.</summary>
    public static bool AnyActive(IReadOnlyList<LayerFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        foreach (var filter in filters)
        {
            if (filter.Enabled && !filter.IsIdentity)
                return true;
        }
        return false;
    }

    /// <summary>Apply the enabled filters of the list, in order.</summary>
    public static void ApplyAll(IReadOnlyList<LayerFilter> filters, PixelBuffer pixels)
    {
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(pixels);
        foreach (var filter in filters)
        {
            if (filter.Enabled && !filter.IsIdentity)
                filter.Apply(pixels);
        }
    }

    protected static string Signed(int value) => value.ToString("+0;−0;0");
}

/// <summary>
/// Brightness and contrast, −100…100 each (0 = unchanged). Brightness bends the tones like a
/// curve (black and white stay put); contrast steepens or flattens them around middle gray.
/// </summary>
public sealed record BrightnessContrastFilter(int Brightness, int Contrast, bool Enabled = true) : LayerFilter(Enabled)
{
    public override string Name => "Brightness/Contrast";
    public override string Summary => $"Brightness {Signed(Brightness)} · Contrast {Signed(Contrast)}";
    public override bool IsIdentity => Brightness == 0 && Contrast == 0;

    public override void Apply(PixelBuffer pixels) => PixelLoop.MapRgb(pixels, BuildCurve());

    /// <summary>The tone curve (input level → output level), the same for R, G and B.</summary>
    public byte[] BuildCurve()
    {
        double b = Math.Clamp(Brightness, -100, 100) / 100.0;
        double c = Math.Clamp(Contrast, -100, 100) / 100.0;
        double exponent = 1 + 2 * Math.Abs(b);
        double slant = Math.Tan((1 + 0.9 * c) * Math.PI / 4); // 0.08 (flat) … 1 … 12.7 (steep)
        var curve = new byte[256];
        for (int v = 0; v < 256; v++)
        {
            double x = v / 255.0;
            x = b >= 0 ? 1 - Math.Pow(1 - x, exponent) : Math.Pow(x, exponent);
            x = (x - 0.5) * slant + 0.5;
            curve[v] = (byte)Math.Clamp(Math.Round(x * 255), 0, 255);
        }
        return curve;
    }
}

/// <summary>
/// Hue shift in degrees (−180…180), saturation and value (−100…100: −100 removes all
/// saturation / goes black, +100 doubles it), in HSV.
/// </summary>
public sealed record HueSaturationFilter(int Hue, int Saturation, int Value, bool Enabled = true) : LayerFilter(Enabled)
{
    public override string Name => "Hue/Saturation";
    public override string Summary => $"Hue {Signed(Hue)}° · Saturation {Signed(Saturation)} · Value {Signed(Value)}";
    public override bool IsIdentity => Hue % 360 == 0 && Saturation == 0 && Value == 0;

    public override void Apply(PixelBuffer pixels)
    {
        float hueShift = Hue % 360;
        float saturation = 1 + Math.Clamp(Saturation, -100, 100) / 100f;
        float value = 1 + Math.Clamp(Value, -100, 100) / 100f;
        PixelLoop.ForEachRow(pixels, row =>
        {
            for (int i = 0; i < row.Length; i += 4)
            {
                if (row[i + 3] == 0) continue;
                var (h, s, v) = Hsv.FromRgb(row[i], row[i + 1], row[i + 2]);
                h += hueShift;
                if (h < 0) h += 360;
                else if (h >= 360) h -= 360;
                s = Math.Min(1f, s * saturation);
                v = Math.Min(1f, v * value);
                var (r, g, b) = Hsv.ToRgb(h, s, v);
                row[i] = r;
                row[i + 1] = g;
                row[i + 2] = b;
            }
        });
    }
}

/// <summary>Color shifts for one tonal range, −100…100 each: toward red / green / blue when positive.</summary>
public readonly record struct ToneShift(int CyanRed, int MagentaGreen, int YellowBlue)
{
    public bool IsZero => CyanRed == 0 && MagentaGreen == 0 && YellowBlue == 0;

    public override string ToString() =>
        $"{CyanRed.ToString("+0;−0;0")} / {MagentaGreen.ToString("+0;−0;0")} / {YellowBlue.ToString("+0;−0;0")}";
}

/// <summary>
/// Color balance: shifts toward cyan–red, magenta–green and yellow–blue, set apart for the
/// shadows, midtones and highlights. Each range fades smoothly into the next (the three weights
/// always add up to 1, so the same shift in all three moves every tone alike). Preserve
/// luminosity keeps each pixel's brightness, changing only its color.
/// </summary>
public sealed record ColorBalanceFilter(
    ToneShift Shadows,
    ToneShift Midtones,
    ToneShift Highlights,
    bool PreserveLuminosity = true,
    bool Enabled = true) : LayerFilter(Enabled)
{
    /// <summary>Largest shift, as a fraction of full scale, for ±100 where a range weighs fully.</summary>
    private const float Strength = 0.75f;

    public override string Name => "Color Balance";

    public override string Summary
    {
        get
        {
            var parts = new List<string>();
            if (!Shadows.IsZero) parts.Add($"Shadows {Shadows}");
            if (!Midtones.IsZero) parts.Add($"Midtones {Midtones}");
            if (!Highlights.IsZero) parts.Add($"Highlights {Highlights}");
            return parts.Count == 0 ? "No change" : string.Join(" · ", parts);
        }
    }

    public override bool IsIdentity => Shadows.IsZero && Midtones.IsZero && Highlights.IsZero;

    public override void Apply(PixelBuffer pixels)
    {
        const float k = Strength / 100f;
        var s = Shadows;
        var m = Midtones;
        var h = Highlights;
        bool preserve = PreserveLuminosity;
        PixelLoop.ForEachRow(pixels, row =>
        {
            for (int i = 0; i < row.Length; i += 4)
            {
                if (row[i + 3] == 0) continue;
                float r = row[i] / 255f, g = row[i + 1] / 255f, b = row[i + 2] / 255f;
                float t = (Math.Max(r, Math.Max(g, b)) + Math.Min(r, Math.Min(g, b))) / 2; // lightness
                float ws = (1 - t) * (1 - t), wm = 2 * t * (1 - t), wh = t * t;
                float nr = Math.Clamp(r + (s.CyanRed * ws + m.CyanRed * wm + h.CyanRed * wh) * k, 0, 1);
                float ng = Math.Clamp(g + (s.MagentaGreen * ws + m.MagentaGreen * wm + h.MagentaGreen * wh) * k, 0, 1);
                float nb = Math.Clamp(b + (s.YellowBlue * ws + m.YellowBlue * wm + h.YellowBlue * wh) * k, 0, 1);
                if (preserve)
                {
                    float d = Luma(r, g, b) - Luma(nr, ng, nb);
                    nr = Math.Clamp(nr + d, 0, 1);
                    ng = Math.Clamp(ng + d, 0, 1);
                    nb = Math.Clamp(nb + d, 0, 1);
                }
                row[i] = (byte)(nr * 255 + 0.5f);
                row[i + 1] = (byte)(ng * 255 + 0.5f);
                row[i + 2] = (byte)(nb * 255 + 0.5f);
            }
        });
    }

    private static float Luma(float r, float g, float b) => 0.299f * r + 0.587f * g + 0.114f * b;
}

/// <summary>RGB ↔ HSV (hue 0–360, saturation and value 0–1).</summary>
internal static class Hsv
{
    public static (float H, float S, float V) FromRgb(byte r8, byte g8, byte b8)
    {
        float r = r8 / 255f, g = g8 / 255f, b = b8 / 255f;
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float delta = max - min;
        float h = 0;
        if (delta > 0)
        {
            if (max == r) h = 60 * ((g - b) / delta);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
            if (h < 0) h += 360;
        }
        return (h, max == 0 ? 0 : delta / max, max);
    }

    public static (byte R, byte G, byte B) ToRgb(float h, float s, float v)
    {
        float c = v * s;
        float hp = h / 60f;
        float x = c * (1 - Math.Abs(hp % 2 - 1));
        float r, g, b;
        switch ((int)hp)
        {
            case 0: (r, g, b) = (c, x, 0); break;
            case 1: (r, g, b) = (x, c, 0); break;
            case 2: (r, g, b) = (0, c, x); break;
            case 3: (r, g, b) = (0, x, c); break;
            case 4: (r, g, b) = (x, 0, c); break;
            default: (r, g, b) = (c, 0, x); break;
        }
        float m = v - c;
        return (ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(float v) => (byte)Math.Clamp(v * 255 + 0.5f, 0, 255);
}

/// <summary>Row loops over RGBA pixels, in parallel for large images.</summary>
internal static class PixelLoop
{
    public delegate void RowAction(Span<byte> row);

    public static void ForEachRow(PixelBuffer pixels, RowAction body)
    {
        int stride = pixels.Stride;
        if ((long)pixels.Width * pixels.Height < 64 * 1024)
        {
            for (int y = 0; y < pixels.Height; y++)
                body(pixels.Pixels.Slice(y * stride, stride));
            return;
        }
        Parallel.For(0, pixels.Height, y => body(pixels.Pixels.Slice(y * stride, stride)));
    }

    /// <summary>Same curve on R, G and B; fully transparent pixels are skipped.</summary>
    public static void MapRgb(PixelBuffer pixels, byte[] curve)
    {
        ForEachRow(pixels, row =>
        {
            for (int i = 0; i < row.Length; i += 4)
            {
                if (row[i + 3] == 0) continue;
                row[i] = curve[row[i]];
                row[i + 1] = curve[row[i + 1]];
                row[i + 2] = curve[row[i + 2]];
            }
        });
    }
}
