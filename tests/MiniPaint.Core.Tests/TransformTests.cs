using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Transform;

namespace MiniPaint.Core.Tests;

public class TransformTests
{
    [Fact]
    public void Bake_Identity_PreservesPixelsRoughly()
    {
        var buf = new PixelBuffer(4, 4);
        buf.FillRect(0, 0, 4, 4, 255, 0, 0, 255);
        var layer = new Layer("L", buf, 0, 0);
        using var session = new FreeTransformSession(layer);
        var (baked, ox, oy) = session.Bake();
        Assert.True(baked.Width >= 4);
        Assert.True(baked.Height >= 4);
        Assert.Equal(0, ox);
        Assert.Equal(0, oy);
        // Center-ish pixel should still be red-ish
        int i = baked.Offset(baked.Width / 2, baked.Height / 2);
        Assert.True(baked.PixelsReadOnly[i] > 200);
        Assert.Equal(255, baked.PixelsReadOnly[i + 3]);
        baked.Dispose();
        layer.Dispose();
    }

    [Fact]
    public void Scale_DoublesExtent()
    {
        var buf = new PixelBuffer(10, 10);
        buf.FillRect(0, 0, 10, 10, 0, 255, 0, 255);
        var layer = new Layer("L", buf, 0, 0);
        using var session = new FreeTransformSession(layer);
        session.ScaleX = 2;
        session.ScaleY = 2;
        var corners = session.GetCorners();
        double w = corners.Max(c => c.x) - corners.Min(c => c.x);
        double h = corners.Max(c => c.y) - corners.Min(c => c.y);
        Assert.Equal(20, w, 1);
        Assert.Equal(20, h, 1);
        layer.Dispose();
    }

    [Fact]
    public void Workspace_CommitTransform_IsUndoable()
    {
        var doc = PaintDocument.CreateBlank("t", 32, 32);
        doc.Layers[0].Pixels.FillRect(4, 4, 8, 8, 0, 0, 255, 255);
        using var ws = new DocumentWorkspace(doc);
        Assert.True(ws.BeginTransform());
        ws.ActiveTransform!.ScaleX = 1.5;
        ws.ActiveTransform.ScaleY = 1.5;
        ws.RefreshTransformPreview();
        int wAfter = doc.Layers[0].Pixels.Width;
        ws.CommitTransform();
        Assert.False(ws.IsTransforming);
        Assert.True(ws.History.CanUndo);
        ws.History.Undo();
        Assert.Equal(32, doc.Layers[0].Pixels.Width); // original blank layer was full doc size
        Assert.NotEqual(0, wAfter);
    }

    [Fact]
    public void ShiftScale_KeepsScaleProportion()
    {
        var buf = new PixelBuffer(20, 10);
        buf.Clear(255, 255, 255, 255);
        var layer = new Layer("L", buf);
        using var session = new FreeTransformSession(layer);
        // Start scales are 1:1 — Shift keeps ScaleX == ScaleY even if drag is anisotropic.
        var ix = session.BeginScale(TransformHandle.SE, session.PivotX + 10, session.PivotY + 5, fromCenter: true);
        ix.Update(session.PivotX + 30, session.PivotY + 8, constrainProportions: true);
        Assert.Equal(session.ScaleX, session.ScaleY, 3);
        layer.Dispose();
    }
}
