using MiniPaint.Core.Composition;
using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Tests;

public class CompositionTests
{
    [Fact]
    public void Composite_BottomToTop_WithOpacity()
    {
        var doc = PaintDocument.CreateBlank("t", 2, 2, transparent: true);
        doc.Layers[0].Pixels.FillRect(0, 0, 2, 2, 255, 0, 0, 255); // red

        var green = new PixelBuffer(2, 2);
        green.FillRect(0, 0, 2, 2, 0, 255, 0, 255);
        var top = new Layer("green", green) { Opacity = 0.5f };
        doc.AddLayer(top);

        using var result = CompositionEngine.Composite(doc);
        var px = result.PixelsReadOnly;
        Assert.True(px[0] > 0);
        Assert.True(px[1] > 0);
        Assert.Equal(255, px[3]);
    }

    [Fact]
    public void Composite_HiddenLayer_Skipped()
    {
        var doc = PaintDocument.CreateBlank("t", 1, 1, transparent: true);
        doc.Layers[0].Pixels.FillRect(0, 0, 1, 1, 255, 0, 0, 255);
        doc.Layers[0].IsVisible = false;

        using var result = CompositionEngine.Composite(doc);
        Assert.Equal(0, result.PixelsReadOnly[3]);
    }

    [Fact]
    public void Composite_OffsetLayer_DrawsAtPosition()
    {
        var doc = new PaintDocument("t", 4, 4);
        var buf = new PixelBuffer(1, 1);
        buf.FillRect(0, 0, 1, 1, 0, 0, 255, 255);
        doc.AddLayer(new Layer("dot", buf, offsetX: 2, offsetY: 3));

        using var result = CompositionEngine.Composite(doc);
        int i = result.Offset(2, 3);
        Assert.Equal(255, result.PixelsReadOnly[i + 2]); // blue
        Assert.Equal(0, result.PixelsReadOnly[result.Offset(0, 0) + 3]);
    }

    [Fact]
    public void CannotDeleteLastLayer()
    {
        var doc = PaintDocument.CreateBlank("t", 2, 2);
        Assert.Throws<InvalidOperationException>(() => doc.RemoveLayer(doc.Layers[0].Id));
    }
}
