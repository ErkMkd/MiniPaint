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
        Assert.Equal(32, doc.Layers[0].Pixels.Width);
        Assert.NotEqual(0, wAfter);
    }

    [Fact]
    public void Workspace_BeginTransform_WithSelection_LiftsContent()
    {
        var doc = PaintDocument.CreateBlank("t", 16, 16, transparent: true);
        var layer = doc.ActiveLayer!;
        layer.Pixels.FillRect(2, 2, 4, 4, 255, 0, 0, 255);
        using var ws = new DocumentWorkspace(doc);
        ws.Selection.Set(2, 2, 4, 4);

        Assert.True(ws.BeginTransform());
        Assert.True(ws.IsSelectionTransform);
        Assert.True(ws.IsTransforming);
        // Hole cut in source
        Assert.Equal(0, layer.Pixels.PixelsReadOnly[layer.Pixels.Offset(2, 2) + 3]);
        // Float layer holds the selection; marquee follows float bounds
        Assert.Equal(4, ws.ActiveTransform!.BaseWidth);
        Assert.Equal(4, ws.ActiveTransform.BaseHeight);
        Assert.True(ws.Selection.HasSelection);
        Assert.Single(doc.Layers); // float is not a document layer
    }

    [Fact]
    public void Workspace_SelectionTransform_Move_CommitsOntoSource()
    {
        var doc = PaintDocument.CreateBlank("t", 16, 16, transparent: true);
        var layer = doc.ActiveLayer!;
        layer.Pixels.FillRect(2, 2, 3, 3, 0, 255, 0, 255);
        using var ws = new DocumentWorkspace(doc);
        ws.Selection.Set(2, 2, 3, 3);

        Assert.True(ws.BeginTransform());
        ws.ActiveTransform!.TranslateX = 5;
        ws.ActiveTransform.TranslateY = 1;
        ws.RefreshTransformPreview();
        ws.CommitTransform();

        Assert.False(ws.IsTransforming);
        Assert.False(ws.IsSelectionTransform);
        Assert.Single(doc.Layers);
        // Original hole empty
        Assert.Equal(0, layer.Pixels.PixelsReadOnly[layer.Pixels.Offset(2, 2) + 3]);
        // Content pasted near new selection origin
        Assert.True(ws.Selection.HasSelection);
        int sx = ws.Selection.X;
        int sy = ws.Selection.Y;
        Assert.True(sx >= 6 && sx <= 8, $"sel X={sx}");
        Assert.True(sy >= 2 && sy <= 4, $"sel Y={sy}");
        Assert.Equal(255, layer.Pixels.PixelsReadOnly[layer.Pixels.Offset(sx, sy) + 3]);
        Assert.Equal(255, layer.Pixels.PixelsReadOnly[layer.Pixels.Offset(sx, sy) + 1]); // green
        Assert.True(ws.History.CanUndo);
        ws.History.Undo();
        Assert.Equal(255, layer.Pixels.PixelsReadOnly[layer.Pixels.Offset(2, 2) + 3]);
        Assert.Equal(0, layer.Pixels.PixelsReadOnly[layer.Pixels.Offset(sx, sy) + 3]);
    }

    [Fact]
    public void Workspace_SelectionTransform_Cancel_Restores()
    {
        var doc = PaintDocument.CreateBlank("t", 12, 12, transparent: true);
        var layer = doc.ActiveLayer!;
        layer.Pixels.FillRect(1, 1, 4, 4, 10, 20, 30, 255);
        using var ws = new DocumentWorkspace(doc);
        ws.Selection.Set(1, 1, 4, 4);

        Assert.True(ws.BeginTransform());
        ws.ActiveTransform!.TranslateX = 3;
        ws.RefreshTransformPreview();
        ws.CancelTransform();

        Assert.False(ws.IsTransforming);
        Assert.False(ws.IsSelectionTransform);
        Assert.Equal(1, ws.Selection.X);
        Assert.Equal(1, ws.Selection.Y);
        Assert.Equal(4, ws.Selection.Width);
        int i = layer.Pixels.Offset(1, 1);
        Assert.Equal(10, layer.Pixels.PixelsReadOnly[i]);
        Assert.Equal(20, layer.Pixels.PixelsReadOnly[i + 1]);
        Assert.Equal(30, layer.Pixels.PixelsReadOnly[i + 2]);
        Assert.Equal(255, layer.Pixels.PixelsReadOnly[i + 3]);
    }

    [Fact]
    public void ShiftScale_KeepsScaleProportion()
    {
        var buf = new PixelBuffer(20, 10);
        buf.Clear(255, 255, 255, 255);
        var layer = new Layer("L", buf);
        using var session = new FreeTransformSession(layer);
        var ix = session.BeginScale(TransformHandle.SE);
        // Drag anisotropic but constrain — scales stay proportional to start (1:1).
        ix.Update(session.PivotX + 30, session.PivotY + 8, constrainProportions: true, fromCenter: true);
        Assert.Equal(session.ScaleX, session.ScaleY, 3);
        layer.Dispose();
    }

    [Fact]
    public void Scale_Default_KeepsOppositeCornerFixed()
    {
        var buf = new PixelBuffer(20, 20);
        buf.Clear(255, 255, 255, 255);
        var layer = new Layer("L", buf, 0, 0);
        using var session = new FreeTransformSession(layer);

        // NW corner at start (doc): (0,0) for layer at 0,0 size 20 → center 10,10; NW local (-10,-10)
        var nwBefore = session.GetHandleDocPoint(TransformHandle.NW);

        var ix = session.BeginScale(TransformHandle.SE);
        // Drag SE outward (fromCenter: false → NW anchored)
        ix.Update(session.PivotX + 30, session.PivotY + 30, constrainProportions: false, fromCenter: false);

        var nwAfter = session.GetHandleDocPoint(TransformHandle.NW);
        Assert.Equal(nwBefore.x, nwAfter.x, 2);
        Assert.Equal(nwBefore.y, nwAfter.y, 2);
        Assert.True(session.ScaleX > 1.2);
        Assert.True(session.ScaleY > 1.2);
        layer.Dispose();
    }

    [Fact]
    public void Scale_Alt_KeepsCenterFixed()
    {
        var buf = new PixelBuffer(20, 20);
        buf.Clear(255, 255, 255, 255);
        var layer = new Layer("L", buf, 0, 0);
        using var session = new FreeTransformSession(layer);

        double cx0 = session.PivotX + session.TranslateX;
        double cy0 = session.PivotY + session.TranslateY;

        var ix = session.BeginScale(TransformHandle.SE);
        ix.Update(session.PivotX + 30, session.PivotY + 30, constrainProportions: false, fromCenter: true);

        double cx1 = session.PivotX + session.TranslateX;
        double cy1 = session.PivotY + session.TranslateY;
        Assert.Equal(cx0, cx1, 2);
        Assert.Equal(cy0, cy1, 2);
        Assert.True(Math.Abs(session.ScaleX) > 1.2);
        layer.Dispose();
    }

    [Fact]
    public void Scale_Edge_KeepsOppositeEdgeFixed()
    {
        var buf = new PixelBuffer(40, 20);
        buf.Clear(0, 0, 255, 255);
        var layer = new Layer("L", buf, 10, 5);
        using var session = new FreeTransformSession(layer);

        var westBefore = session.GetHandleDocPoint(TransformHandle.W);
        var ix = session.BeginScale(TransformHandle.E);
        ix.Update(session.PivotX + session.TranslateX + 40, session.PivotY + session.TranslateY,
            constrainProportions: false, fromCenter: false);

        var westAfter = session.GetHandleDocPoint(TransformHandle.W);
        Assert.Equal(westBefore.x, westAfter.x, 2);
        Assert.Equal(westBefore.y, westAfter.y, 2);
        layer.Dispose();
    }
}
