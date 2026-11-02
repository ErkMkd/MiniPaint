using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Tests;

public class PixelOpsTests
{
    [Fact]
    public void BlendOver_OpaqueSource_ReplacesDestination()
    {
        byte dr = 10, dg = 20, db = 30, da = 255;
        PixelOps.BlendOver(100, 150, 200, 255, ref dr, ref dg, ref db, ref da);
        Assert.Equal(100, dr);
        Assert.Equal(150, dg);
        Assert.Equal(200, db);
        Assert.Equal(255, da);
    }

    [Fact]
    public void BlendOver_TransparentSource_KeepsDestination()
    {
        byte dr = 10, dg = 20, db = 30, da = 255;
        PixelOps.BlendOver(100, 150, 200, 0, ref dr, ref dg, ref db, ref da);
        Assert.Equal(10, dr);
        Assert.Equal(20, dg);
        Assert.Equal(30, db);
        Assert.Equal(255, da);
    }

    [Fact]
    public void BlendOver_SemiTransparent_CompositesAlpha()
    {
        byte dr = 0, dg = 0, db = 0, da = 255;
        PixelOps.BlendOver(255, 0, 0, 128, ref dr, ref dg, ref db, ref da);
        Assert.Equal(255, da);
        Assert.True(dr > 100);
        Assert.True(dg < 30);
    }

    [Fact]
    public void ExtractRegion_RespectsLayerOffset()
    {
        var layer = new PixelBuffer(4, 4);
        layer.FillRect(0, 0, 4, 4, 0, 0, 0, 0);
        layer.FillRect(1, 1, 1, 1, 255, 0, 0, 255); // pixel at layer (1,1)

        // Layer offset (10,10) → document pixel (11,11) is red
        using var extracted = PixelOps.ExtractRegion(layer, 10, 10, 11, 11, 1, 1);
        var px = extracted.PixelsReadOnly;
        Assert.Equal(255, px[0]);
        Assert.Equal(0, px[1]);
        Assert.Equal(0, px[2]);
        Assert.Equal(255, px[3]);
    }

    [Fact]
    public void ScreenToDocument_AndBack_RoundTrip()
    {
        var (dx, dy) = PixelOps.ScreenToDocument(150, 250, panX: 50, panY: 50, zoom: 2);
        Assert.Equal(50, dx);
        Assert.Equal(100, dy);
        var (sx, sy) = PixelOps.DocumentToScreen(dx, dy, 50, 50, 2);
        Assert.Equal(150, sx);
        Assert.Equal(250, sy);
    }

    [Fact]
    public void ZoomAtCursor_KeepsDocumentPointStable()
    {
        double panX = 10, panY = 20, zoom = 1;
        double cx = 100, cy = 100;
        double docX = (cx - panX) / zoom;
        double docY = (cy - panY) / zoom;

        var (npX, npY) = PixelOps.ZoomAtCursor(zoom, 2, cx, cy, panX, panY);
        double docX2 = (cx - npX) / 2;
        double docY2 = (cy - npY) / 2;
        Assert.Equal(docX, docX2, 5);
        Assert.Equal(docY, docY2, 5);
    }

    [Fact]
    public void ClearRegion_MakesPixelsTransparent()
    {
        var buf = new PixelBuffer(8, 8);
        buf.Clear(255, 0, 0, 255);
        PixelOps.ClearRegion(buf, 0, 0, 2, 2, 2, 2);
        var px = buf.PixelsReadOnly;
        int i = buf.Offset(2, 2);
        Assert.Equal(0, px[i + 3]);
        int j = buf.Offset(0, 0);
        Assert.Equal(255, px[j + 3]);
    }
}
