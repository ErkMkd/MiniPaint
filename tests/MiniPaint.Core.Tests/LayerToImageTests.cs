using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Tests;

public class LayerToImageTests
{
    [Fact]
    public void TryGetOpaqueBounds_FindsContent()
    {
        var buf = new PixelBuffer(10, 10);
        buf.FillRect(3, 4, 2, 3, 255, 0, 0, 255);

        Assert.True(PixelOps.TryGetOpaqueBounds(buf, out int x, out int y, out int w, out int h));
        Assert.Equal(3, x);
        Assert.Equal(4, y);
        Assert.Equal(2, w);
        Assert.Equal(3, h);
    }

    [Fact]
    public void TryGetOpaqueBounds_FullyTransparent_ReturnsFalse()
    {
        var buf = new PixelBuffer(4, 4);
        Assert.False(PixelOps.TryGetOpaqueBounds(buf, out _, out _, out _, out _));
    }

    [Fact]
    public void CreateFromLayerContent_TrimsToOpaqueAndOpensSizedDocument()
    {
        var src = PaintDocument.CreateBlank("src", 20, 20, transparent: true);
        var layer = src.ActiveLayer!;
        layer.Name = "Stamp";
        layer.Pixels.FillRect(5, 6, 4, 3, 0, 255, 0, 255);

        using var doc = PaintDocument.CreateFromLayerContent(layer);
        Assert.NotNull(doc);
        Assert.Equal(4, doc!.Width);
        Assert.Equal(3, doc.Height);
        Assert.Equal("Stamp", doc.Name);
        Assert.Single(doc.Layers);
        Assert.Equal(0, doc.Layers[0].OffsetX);
        Assert.Equal(0, doc.Layers[0].OffsetY);

        var px = doc.Layers[0].Pixels.PixelsReadOnly;
        Assert.Equal(255, px[doc.Layers[0].Pixels.Offset(0, 0) + 3]);
        Assert.Equal(0, px[doc.Layers[0].Pixels.Offset(0, 0)]);
        Assert.Equal(255, px[doc.Layers[0].Pixels.Offset(0, 0) + 1]);
    }

    [Fact]
    public void CreateFromLayerContent_EmptyLayer_ReturnsNull()
    {
        var src = PaintDocument.CreateBlank("src", 8, 8, transparent: true);
        Assert.Null(PaintDocument.CreateFromLayerContent(src.ActiveLayer!));
    }

    [Fact]
    public void WorkspaceManager_NewDocumentFromLayerContent_AddsTab()
    {
        using var mgr = new WorkspaceManager();
        var src = mgr.NewDocument("A", 16, 16);
        src.Document.ActiveLayer!.Pixels.FillRect(2, 2, 5, 5, 255, 255, 255, 255);

        var ws = mgr.NewDocumentFromLayerContent(src.Document.ActiveLayer!, "Cropped");
        Assert.NotNull(ws);
        Assert.Equal(2, mgr.Workspaces.Count);
        Assert.Equal(5, ws!.Document.Width);
        Assert.Equal(5, ws.Document.Height);
        Assert.Same(ws, mgr.Active);
    }
}
