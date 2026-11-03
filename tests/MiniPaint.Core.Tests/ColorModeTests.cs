using MiniPaint.Core.ColorMode;
using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Tests;

public class ColorModeTests
{
    [Fact]
    public void EcsSanitize_ForcesHalfBrightAt64()
    {
        var s = new DocumentColorSettings
        {
            Mode = ColorModeKind.Ecs,
            ColorCount = 64,
            HalfBright = false
        };
        s.Sanitize();
        Assert.True(s.HalfBright);
        Assert.Equal(32, s.EffectivePaletteSlots);
    }

    [Fact]
    public void AmigaSnap_4Bit_Has16Levels()
    {
        var a = AmigaColorSpace.SnapChannel(0, 4);
        var b = AmigaColorSpace.SnapChannel(255, 4);
        var mid = AmigaColorSpace.SnapChannel(128, 4);
        Assert.Equal(0, a);
        Assert.Equal(255, b);
        Assert.Equal(AmigaColorSpace.SnapChannel(mid, 4), mid);
    }

    [Theory]
    [InlineData(QuantizerKind.MedianCut)]
    [InlineData(QuantizerKind.Octree)]
    [InlineData(QuantizerKind.Wu)]
    [InlineData(QuantizerKind.KMeansPlusPlus)]
    [InlineData(QuantizerKind.NeuQuant)]
    public void Quantizers_BuildRequestedCount(QuantizerKind kind)
    {
        var buf = new PixelBuffer(16, 16);
        buf.FillRect(0, 0, 8, 8, 255, 0, 0, 255);
        buf.FillRect(8, 0, 8, 8, 0, 255, 0, 255);
        buf.FillRect(0, 8, 8, 8, 0, 0, 255, 255);
        buf.FillRect(8, 8, 8, 8, 255, 255, 0, 255);

        var palette = Quantizers.BuildPalette(buf, 4, bitsPerChannel: 8, kind);
        Assert.Equal(4, palette.Length);
    }

    [Fact]
    public void Pipeline_IndexedRemapsOpaquePixelsToPalette()
    {
        var doc = PaintDocument.CreateBlank("t", 8, 8, transparent: true);
        doc.ActiveLayer!.Pixels.FillRect(0, 0, 8, 8, 200, 10, 10, 255);
        doc.ColorSettings.Mode = ColorModeKind.Indexed;
        doc.ColorSettings.ColorCount = 2;
        doc.ColorSettings.Quantizer = QuantizerKind.MedianCut;
        doc.ColorSettings.Dither = DitherKind.None;

        using var composite = Composition.CompositionEngine.Composite(doc);
        ColorModePipeline.ApplyInPlace(composite, doc.ColorSettings);
        var px = composite.PixelsReadOnly;
        // All opaque pixels should share one of two palette colors
        var colors = new HashSet<(byte, byte, byte)>();
        for (int i = 0; i < px.Length; i += 4)
        {
            if (px[i + 3] == 0) continue;
            colors.Add((px[i], px[i + 1], px[i + 2]));
        }
        Assert.InRange(colors.Count, 1, 2);
    }

    [Fact]
    public void ExtraHalfBrite_ExpandsToDouble()
    {
        var basePal = new PaletteRgb[] { new(255, 0, 0), new(0, 255, 0) };
        var ehb = AmigaColorSpace.ExpandExtraHalfBrite(basePal);
        Assert.Equal(4, ehb.Length);
        Assert.Equal(127, ehb[2].R); // 255/2
        Assert.Equal(0, ehb[2].G);
    }

    [Fact]
    public void Ecs_4096ColorSpace_NibblesRoundTrip()
    {
        Assert.Equal(4096, AmigaColorSpace.EcsColorSpaceSize);
        int distinct = 0;
        var seen = new HashSet<PaletteRgb>();
        for (int r = 0; r < 16; r++)
        for (int g = 0; g < 16; g++)
        for (int b = 0; b < 16; b++)
        {
            var c = AmigaColorSpace.FromNibbles(r, g, b);
            Assert.True(seen.Add(c));
            var (nr, ng, nb) = AmigaColorSpace.ToNibbles(c);
            Assert.Equal(r, nr);
            Assert.Equal(g, ng);
            Assert.Equal(b, nb);
            distinct++;
        }
        Assert.Equal(4096, distinct);
    }

    [Fact]
    public void SetPalette_Ecs_SnapsTo12Bit()
    {
        var s = new DocumentColorSettings { Mode = ColorModeKind.Ecs, ColorCount = 4 };
        s.Sanitize();
        s.SetPalette([new PaletteRgb(10, 20, 30), new(100, 150, 200)], customized: true);
        Assert.True(s.HasValidPalette);
        foreach (var c in s.Palette!)
        {
            var snapped = AmigaColorSpace.SnapColor(c, 4);
            Assert.Equal(snapped, c);
        }
    }
}
