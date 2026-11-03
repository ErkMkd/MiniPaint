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
    public void Composite_PerLayerOpacity_Independent()
    {
        var doc = PaintDocument.CreateBlank("t", 1, 1, transparent: true);
        doc.Layers[0].Pixels.FillRect(0, 0, 1, 1, 255, 0, 0, 255);
        doc.Layers[0].Opacity = 1f;

        var mid = new PixelBuffer(1, 1);
        mid.FillRect(0, 0, 1, 1, 0, 255, 0, 255);
        doc.AddLayer(new Layer("mid", mid) { Opacity = 0.25f });

        var top = new PixelBuffer(1, 1);
        top.FillRect(0, 0, 1, 1, 0, 0, 255, 255);
        doc.AddLayer(new Layer("top", top) { Opacity = 0.75f });

        using var result = CompositionEngine.Composite(doc);
        Assert.Equal(255, result.PixelsReadOnly[3]);
        Assert.True(result.PixelsReadOnly[2] > 100); // blue from top dominates
    }

    [Fact]
    public void Composite_Multiply_Darkens()
    {
        var doc = PaintDocument.CreateBlank("t", 1, 1, transparent: true);
        doc.Layers[0].Pixels.FillRect(0, 0, 1, 1, 200, 200, 200, 255);

        var overlay = new PixelBuffer(1, 1);
        overlay.FillRect(0, 0, 1, 1, 100, 100, 100, 255);
        doc.AddLayer(new Layer("mul", overlay) { BlendMode = BlendMode.Multiply });

        using var result = CompositionEngine.Composite(doc);
        var px = result.PixelsReadOnly;
        // 200/255 * 100/255 ≈ 78
        Assert.InRange(px[0], 70, 90);
        Assert.InRange(px[1], 70, 90);
        Assert.InRange(px[2], 70, 90);
        Assert.Equal(255, px[3]);
    }

    [Fact]
    public void Composite_Screen_Lightens()
    {
        var doc = PaintDocument.CreateBlank("t", 1, 1, transparent: true);
        doc.Layers[0].Pixels.FillRect(0, 0, 1, 1, 50, 50, 50, 255);

        var overlay = new PixelBuffer(1, 1);
        overlay.FillRect(0, 0, 1, 1, 100, 100, 100, 255);
        doc.AddLayer(new Layer("scr", overlay) { BlendMode = BlendMode.Screen });

        using var result = CompositionEngine.Composite(doc);
        var px = result.PixelsReadOnly;
        Assert.True(px[0] > 100);
        Assert.Equal(255, px[3]);
    }

    [Fact]
    public void Composite_ZeroOpacity_Skipped()
    {
        var doc = PaintDocument.CreateBlank("t", 1, 1, transparent: true);
        doc.Layers[0].Pixels.FillRect(0, 0, 1, 1, 255, 0, 0, 255);

        var top = new PixelBuffer(1, 1);
        top.FillRect(0, 0, 1, 1, 0, 255, 0, 255);
        doc.AddLayer(new Layer("invisible-op", top) { Opacity = 0f });

        using var result = CompositionEngine.Composite(doc);
        Assert.Equal(255, result.PixelsReadOnly[0]);
        Assert.Equal(0, result.PixelsReadOnly[1]);
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
