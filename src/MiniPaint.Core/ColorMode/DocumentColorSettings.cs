namespace MiniPaint.Core.ColorMode;

/// <summary>
/// Live document color constraint (Amiga-style modes + modern indexed).
/// Layers stay full RGBA; the composite is remapped for display/export.
/// </summary>
public sealed class DocumentColorSettings : IEquatable<DocumentColorSettings>
{
    private PaletteRgb[]? _palette;

    public ColorModeKind Mode { get; set; } = ColorModeKind.Rgba;
    public int ColorCount { get; set; } = 256;
    public bool HalfBright { get; set; }
    public QuantizerKind Quantizer { get; set; } = QuantizerKind.MedianCut;
    public DitherKind Dither { get; set; } = DitherKind.None;

    /// <summary>True when the user edited the palette (rebuild skips auto-replace until cleared).</summary>
    public bool PaletteCustomized { get; set; }

    /// <summary>Unique palette entries (not including Extra Half-Brite mirrors).</summary>
    public IReadOnlyList<PaletteRgb>? Palette => _palette;

    public DocumentColorSettings Clone()
    {
        var c = new DocumentColorSettings
        {
            Mode = Mode,
            ColorCount = ColorCount,
            HalfBright = HalfBright,
            Quantizer = Quantizer,
            Dither = Dither,
            PaletteCustomized = PaletteCustomized
        };
        if (_palette is not null)
            c._palette = (PaletteRgb[])_palette.Clone();
        return c;
    }

    public void CopyFrom(DocumentColorSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Mode = other.Mode;
        ColorCount = other.ColorCount;
        HalfBright = other.HalfBright;
        Quantizer = other.Quantizer;
        Dither = other.Dither;
        PaletteCustomized = other.PaletteCustomized;
        _palette = other._palette is null ? null : (PaletteRgb[])other._palette.Clone();
    }

    public void ClearPalette()
    {
        _palette = null;
        PaletteCustomized = false;
    }

    public void SetPalette(IReadOnlyList<PaletteRgb> colors, bool customized)
    {
        ArgumentNullException.ThrowIfNull(colors);
        Sanitize();
        int n = EffectivePaletteSlots;
        if (n <= 0)
        {
            _palette = null;
            PaletteCustomized = false;
            return;
        }

        var copy = new PaletteRgb[n];
        int bits = BitsPerChannel;
        for (int i = 0; i < n; i++)
        {
            var src = i < colors.Count ? colors[i] : new PaletteRgb(0, 0, 0);
            copy[i] = AmigaColorSpace.SnapColor(src, bits);
        }
        _palette = copy;
        PaletteCustomized = customized;
    }

    public bool HasValidPalette
    {
        get
        {
            Sanitize();
            int n = EffectivePaletteSlots;
            return n > 0 && _palette is not null && _palette.Length == n;
        }
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
                HalfBright = ColorCount == 64;
                break;

            case ColorModeKind.Aga:
                ColorCount = NormalizePowerOfTwo(ColorCount, 2, 256);
                HalfBright = false;
                break;
        }

        if (Mode == ColorModeKind.Rgba)
        {
            _palette = null;
            PaletteCustomized = false;
        }
        else if (_palette is not null && _palette.Length != EffectivePaletteSlots)
        {
            // Size mismatch (mode/count changed) — drop stale palette.
            _palette = null;
            PaletteCustomized = false;
        }
    }

    public int EffectivePaletteSlots
    {
        get
        {
            // Intentionally no Sanitize() recursion here for EffectivePaletteSlots used inside Sanitize.
            return Mode switch
            {
                ColorModeKind.Rgba => 0,
                ColorModeKind.Ecs when ColorCount == 64 => 32,
                ColorModeKind.Indexed => Math.Clamp(ColorCount, 2, 256),
                ColorModeKind.Ecs => NormalizePowerOfTwo(ColorCount, 2, 64) == 64
                    ? 32
                    : NormalizePowerOfTwo(ColorCount, 2, 32),
                ColorModeKind.Aga => NormalizePowerOfTwo(ColorCount, 2, 256),
                _ => 0
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

    public bool Equals(DocumentColorSettings? other)
    {
        if (other is null) return false;
        if (Mode != other.Mode ||
            ColorCount != other.ColorCount ||
            HalfBright != other.HalfBright ||
            Quantizer != other.Quantizer ||
            Dither != other.Dither ||
            PaletteCustomized != other.PaletteCustomized)
            return false;

        if (_palette is null && other._palette is null) return true;
        if (_palette is null || other._palette is null) return false;
        if (_palette.Length != other._palette.Length) return false;
        for (int i = 0; i < _palette.Length; i++)
            if (!_palette[i].Equals(other._palette[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is DocumentColorSettings s && Equals(s);

    public override int GetHashCode()
    {
        var hash = HashCode.Combine(Mode, ColorCount, HalfBright, Quantizer, Dither, PaletteCustomized);
        if (_palette is not null)
        {
            foreach (var c in _palette)
                hash = HashCode.Combine(hash, c);
        }
        return hash;
    }
}
