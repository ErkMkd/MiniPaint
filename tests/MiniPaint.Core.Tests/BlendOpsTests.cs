using MiniPaint.Core.Composition;
using MiniPaint.Core.Document;
using MiniPaint.Core.History;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Tests;

public class BlendOpsTests
{
    [Theory]
    [InlineData(BlendMode.Normal)]
    [InlineData(BlendMode.Multiply)]
    [InlineData(BlendMode.Screen)]
    [InlineData(BlendMode.Overlay)]
    [InlineData(BlendMode.SoftLight)]
    [InlineData(BlendMode.HardLight)]
    [InlineData(BlendMode.ColorDodge)]
    [InlineData(BlendMode.ColorBurn)]
    [InlineData(BlendMode.Darken)]
    [InlineData(BlendMode.Lighten)]
    [InlineData(BlendMode.Difference)]
    [InlineData(BlendMode.Exclusion)]
    [InlineData(BlendMode.Hue)]
    [InlineData(BlendMode.Saturation)]
    [InlineData(BlendMode.Color)]
    [InlineData(BlendMode.Luminosity)]
    [InlineData(BlendMode.LinearBurn)]
    [InlineData(BlendMode.LinearDodge)]
    [InlineData(BlendMode.VividLight)]
    [InlineData(BlendMode.LinearLight)]
    [InlineData(BlendMode.PinLight)]
    [InlineData(BlendMode.HardMix)]
    [InlineData(BlendMode.Dissolve)]
    public void BlendPixel_AllModes_ProduceOpaqueResult(BlendMode mode)
    {
        byte dr = 180, dg = 90, db = 40, da = 255;
        BlendOps.BlendPixel(mode, 40, 160, 220, 255, ref dr, ref dg, ref db, ref da, 1f);
        Assert.Equal(255, da);
        Assert.InRange(dr, 0, 255);
        Assert.InRange(dg, 0, 255);
        Assert.InRange(db, 0, 255);
    }

    [Fact]
    public void Separable_Multiply_IsProduct()
    {
        Assert.Equal(0.25f, BlendOps.Separable(BlendMode.Multiply, 0.5f, 0.5f), 4);
    }

    [Fact]
    public void Separable_Screen_IsComplementProduct()
    {
        Assert.Equal(0.75f, BlendOps.Separable(BlendMode.Screen, 0.5f, 0.5f), 4);
    }

    [Fact]
    public void BlendModeNames_RoundTripParse()
    {
        foreach (var opt in BlendModeNames.All)
        {
            Assert.True(BlendModeNames.TryParse(opt.Mode.ToString(), out var mode));
            Assert.Equal(opt.Mode, mode);
            Assert.True(BlendModeNames.TryParse(opt.Label, out mode));
            Assert.Equal(opt.Mode, mode);
        }
    }

    [Fact]
    public void LayerPropertyCommand_BlendMode_UndoRedo()
    {
        var doc = PaintDocument.CreateBlank("t", 2, 2);
        var layer = doc.Layers[0];
        layer.BlendMode = BlendMode.Normal;

        var history = new HistoryStack();
        history.Execute(LayerPropertyCommand.BlendModeChange(layer, BlendMode.Overlay));
        Assert.Equal(BlendMode.Overlay, layer.BlendMode);

        history.Undo();
        Assert.Equal(BlendMode.Normal, layer.BlendMode);

        history.Redo();
        Assert.Equal(BlendMode.Overlay, layer.BlendMode);
    }

    [Fact]
    public void LayerPropertyCommand_OpacityFromTo_Undo()
    {
        var doc = PaintDocument.CreateBlank("t", 2, 2);
        var layer = doc.Layers[0];
        layer.Opacity = 0.3f; // simulate live preview already applied

        var history = new HistoryStack();
        history.Execute(LayerPropertyCommand.Opacity(layer, from: 1f, to: 0.3f));
        Assert.Equal(0.3f, layer.Opacity, 3);

        history.Undo();
        Assert.Equal(1f, layer.Opacity, 3);
    }

    [Fact]
    public void BlendLayer_WithOpacityAndMode()
    {
        var dst = new PixelBuffer(1, 1);
        dst.FillRect(0, 0, 1, 1, 255, 255, 255, 255);
        var src = new PixelBuffer(1, 1);
        src.FillRect(0, 0, 1, 1, 0, 0, 0, 255);

        PixelOps.BlendLayer(dst, src, 0, 0, 0.5f, BlendMode.Multiply);
        var px = dst.PixelsReadOnly;
        // white * black * 0.5 opacity → gray
        Assert.True(px[0] > 100 && px[0] < 200);
        Assert.Equal(255, px[3]);
    }
}
