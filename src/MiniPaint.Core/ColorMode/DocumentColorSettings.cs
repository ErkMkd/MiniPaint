namespace MiniPaint.Core.ColorMode;

/// <summary>
/// Live document color constraint (Amiga-style modes + modern indexed).
/// Layers stay full RGBA; the composite is remapped for display/export.
/// </summary>
public sealed class DocumentColorSettings : IEquatable<DocumentColorSettings>
{
    public ColorModeKind Mode { get; set; } = ColorModeKind.Rgba;
    public int ColorCount { get; set; } = 256;
    public bool HalfBright { get; set; }
    public QuantizerKind Quantizer { get; set; } = QuantizerKind.MedianCut;
    public DitherKind Dither { get; set; } = DitherKind.None;

    public DocumentColorSettings Clone() => new()
    {
        Mode = Mode,
        ColorCount = ColorCount,
        HalfBright = HalfBright,
        Quantizer = Quantizer,
        Dither = Dither
    };

    public void CopyFrom(DocumentColorSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Mode = other.Mode;
        ColorCount = other.ColorCount;
        HalfBright = other.HalfBright;
        Quantizer = other.Quantizer;
        Dither = other.Dither;
    }

    /// <summary>Normalize counts/flags for the selected mode.</summary>
    public void Sanitize()
    {
        switch (Mode)
        {
            case ColorModeKind.Rgba:
                ColorCount = 0;
                HalfBright = false;
                break;

            case ColorModeKind.Indexed:
                ColorCount = Math.Clamp(ColorCount, 2, 256);
                HalfBright = false;
                break;

            case ColorModeKind.Ecs:
                ColorCount = NormalizePowerOfTwo(ColorCount, 2, 64);
                if (ColorCount == 64)
                    HalfBright = true;
                else
                    HalfBright = false;
                break;

            case ColorModeKind.Aga:
                ColorCount = NormalizePowerOfTwo(ColorCount, 2, 256);
                HalfBright = false;
                break;
        }
    }

    public int EffectivePaletteSlots
    {
        get
        {
            Sanitize();
            return Mode switch
            {
                ColorModeKind.Rgba => 0,
                ColorModeKind.Ecs when HalfBright && ColorCount == 64 => 32, // unique entries; 32 EHB mirrors
                _ => ColorCount
            };
        }
    }

    public int BitsPerChannel => Mode switch
    {
        ColorModeKind.Ecs => 4,
        ColorModeKind.Aga => 8,
        ColorModeKind.Indexed => 8,
        _ => 8
    };

    public static int NormalizePowerOfTwo(int value, int min, int max)
    {
        value = Math.Clamp(value, min, max);
        int p = min;
        while (p * 2 <= value && p * 2 <= max)
            p *= 2;
        return p;
    }

    public bool Equals(DocumentColorSettings? other) =>
        other is not null &&
        Mode == other.Mode &&
        ColorCount == other.ColorCount &&
        HalfBright == other.HalfBright &&
        Quantizer == other.Quantizer &&
        Dither == other.Dither;

    public override bool Equals(object? obj) => obj is DocumentColorSettings s && Equals(s);
    public override int GetHashCode() => HashCode.Combine(Mode, ColorCount, HalfBright, Quantizer, Dither);
}
