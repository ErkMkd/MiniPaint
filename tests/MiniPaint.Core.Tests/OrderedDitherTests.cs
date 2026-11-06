using MiniPaint.Core.ColorMode;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Tests;

public class OrderedDitherTests
{
    private static DocumentColorSettings BlackWhite(DitherKind dither)
    {
        var s = new DocumentColorSettings { Mode = ColorModeKind.Indexed, ColorCount = 2, Dither = dither };
        s.SetPalette([new PaletteRgb(0, 0, 0), new PaletteRgb(255, 255, 255)], customized: true);
        return s;
    }

    private static double WhiteFraction(PixelBuffer px)
    {
        var p = px.PixelsReadOnly;
        int white = 0;
        for (int i = 0; i < p.Length; i += 4)
            if (p[i] == 255) white++;
        return white / (double)(px.Width * px.Height);
    }

    [Theory]
    [InlineData(DitherKind.Bayer4x4)]
    [InlineData(DitherKind.Bayer8x8)]
    public void Bayer_QuarterGray_OnBlackWhite_IsQuarterWhite(DitherKind dither)
    {
        // Before the fix the threshold spread was ±24 levels: 25% gray came out solid black.
        using var px = new PixelBuffer(16, 16);
        px.FillRect(0, 0, 16, 16, 64, 64, 64, 255);

        ColorModePipeline.ApplyInPlace(px, BlackWhite(dither));

        Assert.Equal(0.25, WhiteFraction(px), 3);
    }

    [Fact]
    public void Bayer8x8_DiffersFrom4x4_With32ColorPalette()
    {
        // 32 colors 85 levels apart (R × G × B = 4 × 4 × 2). Before the fix any palette of 32+
        // colors got a ±4 level spread, and Bayer 8×8 rounded to exactly the 4×4 image.
        var palette = new List<PaletteRgb>();
        foreach (byte b in new byte[] { 0, 255 })
            for (int g = 0; g < 4; g++)
                for (int r = 0; r < 4; r++)
                    palette.Add(new PaletteRgb((byte)(r * 85), (byte)(g * 85), b));

        using var src = new PixelBuffer(256, 16);
        var p = src.Pixels;
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 256; x++)
            {
                int i = (y * 256 + x) * 4;
                p[i] = p[i + 1] = p[i + 2] = (byte)x;
                p[i + 3] = 255;
            }

        byte[] Dither(DitherKind kind)
        {
            var s = new DocumentColorSettings { Mode = ColorModeKind.Indexed, ColorCount = 32, Dither = kind };
            s.SetPalette(palette, customized: true);
            using var work = src.Clone();
            ColorModePipeline.ApplyInPlace(work, s);
            return work.PixelsReadOnly.ToArray();
        }

        Assert.False(Dither(DitherKind.Bayer4x4).SequenceEqual(Dither(DitherKind.Bayer8x8)));
    }
}
