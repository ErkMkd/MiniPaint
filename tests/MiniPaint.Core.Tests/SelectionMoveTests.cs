using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Selection;

namespace MiniPaint.Core.Tests;

public class SelectionMoveTests
{
    [Fact]
    public void CutAndPaste_MovesSelectedPixelsWithMarquee()
    {
        var doc = PaintDocument.CreateBlank("t", 8, 8, transparent: true);
        var layer = doc.ActiveLayer!;
        layer.Pixels.FillRect(2, 2, 2, 2, 255, 0, 0, 255);

        var sel = new SelectionState();
        sel.Set(2, 2, 2, 2);

        using var lifted = SelectionOps.CutFromActiveLayer(doc, sel);
        Assert.NotNull(lifted);

        // Hole at origin
        Assert.Equal(0, layer.Pixels.PixelsReadOnly[layer.Pixels.Offset(2, 2) + 3]);

        // Move marquee then paste
        sel.Set(4, 3, 2, 2);
        PixelOps.PasteOver(layer.Pixels, layer.OffsetX, layer.OffsetY, lifted!, sel.X, sel.Y);

        var px = layer.Pixels.PixelsReadOnly;
        Assert.Equal(255, px[layer.Pixels.Offset(4, 3) + 3]);
        Assert.Equal(255, px[layer.Pixels.Offset(4, 3)]);
        Assert.Equal(0, px[layer.Pixels.Offset(2, 2) + 3]);
    }

    [Fact]
    public void CutAndPaste_SamePosition_RestoresPixels()
    {
        var doc = PaintDocument.CreateBlank("t", 4, 4, transparent: true);
        var layer = doc.ActiveLayer!;
        layer.Pixels.FillRect(1, 1, 2, 2, 10, 20, 30, 255);

        var sel = new SelectionState();
        sel.Set(1, 1, 2, 2);

        using var lifted = SelectionOps.CutFromActiveLayer(doc, sel);
        Assert.NotNull(lifted);
        PixelOps.PasteOver(layer.Pixels, 0, 0, lifted!, 1, 1);

        var px = layer.Pixels.PixelsReadOnly;
        int i = layer.Pixels.Offset(1, 1);
        Assert.Equal(10, px[i]);
        Assert.Equal(20, px[i + 1]);
        Assert.Equal(30, px[i + 2]);
        Assert.Equal(255, px[i + 3]);
    }
}
