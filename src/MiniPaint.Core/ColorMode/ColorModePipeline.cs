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

        if (settings.Dither == DitherKind.FloydSteinberg)
            MapFloydSteinberg(buffer, displayPalette, settings.BitsPerChannel);
        else
            MapNearest(buffer, displayPalette, settings.BitsPerChannel);
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
        var px = buffer.PixelsReadOnly;
        int bits = settings.BitsPerChannel;

        if (settings.Dither == DitherKind.FloydSteinberg)
        {
            using var work = buffer.Clone();
            ApplyInPlace(work, settings);
            var wpx = work.PixelsReadOnly;
            for (int i = 0, p = 0; i < wpx.Length; i += 4, p++)
            {
                if (wpx[i + 3] == 0) { indices[p] = 0; continue; }
                indices[p] = (byte)NearestColor.Find(palette, wpx[i], wpx[i + 1], wpx[i + 2]);
            }
        }
        else
        {
            for (int i = 0, p = 0; i < px.Length; i += 4, p++)
            {
                if (px[i + 3] == 0) { indices[p] = 0; continue; }
                var s = AmigaColorSpace.SnapColor(px[i], px[i + 1], px[i + 2], bits);
                indices[p] = (byte)NearestColor.Find(palette, s.R, s.G, s.B);
            }
        }

        return (indices, palette);
    }

    public static bool IsIlbmCompatible(DocumentColorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Sanitize();
        return settings.Mode is ColorModeKind.Indexed or ColorModeKind.Ecs or ColorModeKind.Aga;
    }

    private static void MapNearest(PixelBuffer buffer, IReadOnlyList<PaletteRgb> palette, int bits)
    {
        var px = buffer.Pixels;
        for (int i = 0; i < px.Length; i += 4)
        {
            if (px[i + 3] == 0) continue;
            var snapped = AmigaColorSpace.SnapColor(px[i], px[i + 1], px[i + 2], bits);
            int idx = NearestColor.Find(palette, snapped.R, snapped.G, snapped.B);
            var c = palette[idx];
            px[i] = c.R;
            px[i + 1] = c.G;
            px[i + 2] = c.B;
        }
    }

    private static void MapFloydSteinberg(PixelBuffer buffer, IReadOnlyList<PaletteRgb> palette, int bits)
    {
        int w = buffer.Width;
        int h = buffer.Height;
        var px = buffer.Pixels;

        var r = new float[w * h];
        var g = new float[w * h];
        var b = new float[w * h];
        var opaque = new bool[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                int p = y * w + x;
                if (px[i + 3] == 0) continue;
                opaque[p] = true;
                var s = AmigaColorSpace.SnapColor(px[i], px[i + 1], px[i + 2], bits);
                r[p] = s.R;
                g[p] = s.G;
                b[p] = s.B;
            }
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int p = y * w + x;
                if (!opaque[p]) continue;

                byte nr = (byte)Math.Clamp((int)Math.Round(r[p]), 0, 255);
                byte ng = (byte)Math.Clamp((int)Math.Round(g[p]), 0, 255);
                byte nb = (byte)Math.Clamp((int)Math.Round(b[p]), 0, 255);
                int idx = NearestColor.Find(palette, nr, ng, nb);
                var c = palette[idx];

                float er = r[p] - c.R;
                float eg = g[p] - c.G;
                float eb = b[p] - c.B;

                int pi = p * 4;
                px[pi] = c.R;
                px[pi + 1] = c.G;
                px[pi + 2] = c.B;

                Diffuse(x + 1, y, er, eg, eb, 7f / 16f);
                Diffuse(x - 1, y + 1, er, eg, eb, 3f / 16f);
                Diffuse(x, y + 1, er, eg, eb, 5f / 16f);
                Diffuse(x + 1, y + 1, er, eg, eb, 1f / 16f);
            }
        }

        void Diffuse(int x, int y, float er, float eg, float eb, float weight)
        {
            if ((uint)x >= (uint)w || (uint)y >= (uint)h) return;
            int p = y * w + x;
            if (!opaque[p]) return;
            r[p] += er * weight;
            g[p] += eg * weight;
            b[p] += eb * weight;
        }
    }
}
