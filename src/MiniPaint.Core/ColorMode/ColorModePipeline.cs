using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.ColorMode;

/// <summary>
/// Remap a full-RGBA composite into the document's live color mode (palette + optional dither).
/// </summary>
public static class ColorModePipeline
{
    public static void ApplyInPlace(PixelBuffer buffer, DocumentColorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Sanitize();
        if (settings.Mode == ColorModeKind.Rgba) return;

        var displayPalette = ResolveDisplayPalette(buffer, settings, storeIfBuilt: true);
        Dithering.Apply(buffer, displayPalette, settings.BitsPerChannel, settings.Dither);
    }

    /// <summary>
    /// Unique palette (no EHB mirrors). Builds via quantizer when missing unless customized empty.
    /// </summary>
    public static PaletteRgb[] EnsureUniquePalette(
        PixelBuffer buffer, DocumentColorSettings settings, bool forceRebuild = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Sanitize();
        if (settings.Mode == ColorModeKind.Rgba)
            return Array.Empty<PaletteRgb>();

        if (!forceRebuild && settings.HasValidPalette && settings.Palette is not null)
            return settings.Palette.ToArray();

        var unique = Quantizers.BuildPalette(
            buffer, settings.EffectivePaletteSlots, settings.BitsPerChannel, settings.Quantizer);
        settings.SetPalette(unique, customized: false);
        return unique;
    }

    public static PaletteRgb[] ResolveDisplayPalette(
        PixelBuffer buffer, DocumentColorSettings settings, bool storeIfBuilt = false)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Sanitize();
        if (settings.Mode == ColorModeKind.Rgba)
            return Array.Empty<PaletteRgb>();

        PaletteRgb[] unique;
        if (settings.HasValidPalette && settings.Palette is not null)
        {
            unique = settings.Palette.ToArray();
        }
        else
        {
            unique = Quantizers.BuildPalette(
                buffer, settings.EffectivePaletteSlots, settings.BitsPerChannel, settings.Quantizer);
            if (storeIfBuilt)
                settings.SetPalette(unique, customized: false);
        }

        return settings.Mode == ColorModeKind.Ecs && settings.HalfBright
            ? AmigaColorSpace.ExpandExtraHalfBrite(unique)
            : unique;
    }

    public static PaletteRgb[] BuildDisplayPalette(PixelBuffer buffer, DocumentColorSettings settings) =>
        ResolveDisplayPalette(buffer, settings, storeIfBuilt: false);

    /// <summary>
    /// Quantize to palette indices (for IFF-ILBM export). Throws if mode is RGBA.
    /// </summary>
    public static (byte[] indices, PaletteRgb[] palette) QuantizeToIndexed(
        PixelBuffer buffer, DocumentColorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Sanitize();
        if (settings.Mode == ColorModeKind.Rgba)
            throw new InvalidOperationException("IFF-ILBM export requires Indexed, ECS, or AGA mode.");

        var palette = ResolveDisplayPalette(buffer, settings, storeIfBuilt: true);
        int w = buffer.Width;
        int h = buffer.Height;
        var indices = new byte[w * h];

        using var work = buffer.Clone();
        Dithering.Apply(work, palette, settings.BitsPerChannel, settings.Dither);
        var lookup = new PaletteLookup(palette);
        var wpx = work.PixelsReadOnly;
        for (int i = 0, p = 0; i < wpx.Length; i += 4, p++)
        {
            if (wpx[i + 3] == 0) { indices[p] = 0; continue; }
            indices[p] = (byte)lookup.Find(wpx[i], wpx[i + 1], wpx[i + 2]);
        }

        return (indices, palette);
    }

    public static bool IsIlbmCompatible(DocumentColorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Sanitize();
        return settings.Mode is ColorModeKind.Indexed or ColorModeKind.Ecs or ColorModeKind.Aga;
    }
}
