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
}
