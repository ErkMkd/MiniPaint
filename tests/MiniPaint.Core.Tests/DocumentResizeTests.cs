using MiniPaint.Core.Document;
using MiniPaint.Core.History;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Tests;

public class DocumentResizeTests
{
    [Fact]
    public void CanvasResize_CenterExpand_ShiftsOffsetsByHalf()
    {
        var doc = PaintDocument.CreateBlank("t", 100, 80, transparent: true);
        doc.Layers[0].Pixels.FillRect(0, 0, 10, 10, 255, 0, 0, 255);
        doc.SetLayerOffset(doc.Layers[0].Id, 10, 10);

        DocumentResize.ResizeCanvas(doc, 120, 100, CanvasAnchor.Center);

        Assert.Equal(120, doc.Width);
        Assert.Equal(100, doc.Height);
        Assert.Equal(20, doc.Layers[0].OffsetX); // +10
        Assert.Equal(20, doc.Layers[0].OffsetY); // +10
        Assert.Equal(100, doc.Layers[0].Pixels.Width); // pixels unchanged
        Assert.Equal(80, doc.Layers[0].Pixels.Height);
    }

    [Fact]
    public void CanvasResize_NorthWest_KeepsOffsets()
    {
        var doc = PaintDocument.CreateBlank("t", 50, 50, transparent: true);
        doc.SetLayerOffset(doc.Layers[0].Id, 5, 7);

        DocumentResize.ResizeCanvas(doc, 80, 60, CanvasAnchor.NorthWest);

        Assert.Equal(5, doc.Layers[0].OffsetX);
        Assert.Equal(7, doc.Layers[0].OffsetY);
        Assert.Equal(80, doc.Width);
        Assert.Equal(60, doc.Height);
    }

    [Fact]
    public void CanvasResize_SouthEast_Shrink_MovesContent()
    {
        var doc = PaintDocument.CreateBlank("t", 100, 100, transparent: true);
        doc.SetLayerOffset(doc.Layers[0].Id, 20, 20);

        DocumentResize.ResizeCanvas(doc, 80, 80, CanvasAnchor.SouthEast);

        // delta = -20 → SE shift = -20
        Assert.Equal(0, doc.Layers[0].OffsetX);
        Assert.Equal(0, doc.Layers[0].OffsetY);
        Assert.Equal(80, doc.Width);
    }

    [Theory]
    [InlineData(CanvasAnchor.NorthWest, 0, 0)]
    [InlineData(CanvasAnchor.North, 10, 0)]
    [InlineData(CanvasAnchor.NorthEast, 20, 0)]
    [InlineData(CanvasAnchor.West, 0, 10)]
    [InlineData(CanvasAnchor.Center, 10, 10)]
    [InlineData(CanvasAnchor.East, 20, 10)]
    [InlineData(CanvasAnchor.SouthWest, 0, 20)]
    [InlineData(CanvasAnchor.South, 10, 20)]
    [InlineData(CanvasAnchor.SouthEast, 20, 20)]
    public void CanvasAnchor_OffsetDelta_AllNine(CanvasAnchor anchor, int expectDx, int expectDy)
    {
        var (dx, dy) = CanvasAnchorMath.OffsetDelta(anchor, 100, 100, 120, 120);
        Assert.Equal(expectDx, dx);
        Assert.Equal(expectDy, dy);
    }

    [Fact]
    public void ImageResize_ScalesLayerPixelsAndOffsets()
    {
        var doc = PaintDocument.CreateBlank("t", 100, 50, transparent: true);
        doc.Layers[0].Pixels.FillRect(0, 0, 100, 50, 0, 128, 255, 255);
        doc.SetLayerOffset(doc.Layers[0].Id, 10, 4);

        DocumentResize.ResizeImage(doc, 200, 100);

        Assert.Equal(200, doc.Width);
        Assert.Equal(100, doc.Height);
        Assert.Equal(200, doc.Layers[0].Pixels.Width);
        Assert.Equal(100, doc.Layers[0].Pixels.Height);
        Assert.Equal(20, doc.Layers[0].OffsetX);
        Assert.Equal(8, doc.Layers[0].OffsetY);

        // Opaque sample still present after cubic resample
        var px = doc.Layers[0].Pixels.PixelsReadOnly;
        int mid = doc.Layers[0].Pixels.Offset(100, 50);
        Assert.True(px[mid + 3] > 200);
    }

    [Fact]
    public void ImageResize_DoesNotChangeWhenSameSize()
    {
        var doc = PaintDocument.CreateBlank("t", 32, 32, transparent: true);
        var before = doc.Layers[0].Pixels;
        DocumentResize.ResizeImage(doc, 32, 32);
        Assert.Same(before, doc.Layers[0].Pixels);
    }

    [Fact]
    public void DocumentResizeCommand_Canvas_UndoRedo()
    {
        var doc = PaintDocument.CreateBlank("t", 40, 40, transparent: true);
        doc.SetLayerOffset(doc.Layers[0].Id, 0, 0);
        var history = new HistoryStack();

        var cmd = DocumentResizeCommand.CanvasSize(doc, 60, 40, CanvasAnchor.West);
        history.Record(cmd);

        Assert.Equal(60, doc.Width);
        Assert.Equal(0, doc.Layers[0].OffsetX);

        history.Undo();
        Assert.Equal(40, doc.Width);
        Assert.Equal(40, doc.Layers[0].Pixels.Width);

        history.Redo();
        Assert.Equal(60, doc.Width);
    }

    [Fact]
    public void DocumentResizeCommand_Image_UndoRestoresPixels()
    {
        var doc = PaintDocument.CreateBlank("t", 16, 16, transparent: true);
        doc.Layers[0].Pixels.FillRect(0, 0, 16, 16, 255, 0, 0, 255);
        var history = new HistoryStack();

        var cmd = DocumentResizeCommand.ImageSize(doc, 8, 8);
        history.Record(cmd);
        Assert.Equal(8, doc.Layers[0].Pixels.Width);

        history.Undo();
        Assert.Equal(16, doc.Width);
        Assert.Equal(16, doc.Layers[0].Pixels.Width);
        Assert.Equal(255, doc.Layers[0].Pixels.PixelsReadOnly[0]);
    }

    [Fact]
    public void ScaleBuffer_NearestSize_ProducesOpaqueOutput()
    {
        var src = new PixelBuffer(4, 4);
        src.FillRect(0, 0, 4, 4, 10, 20, 30, 255);
        using var dst = PixelOps.ScaleBuffer(src, 8, 8);
        Assert.Equal(8, dst.Width);
        Assert.Equal(8, dst.Height);
        Assert.Equal(255, dst.PixelsReadOnly[3]);
    }
}
