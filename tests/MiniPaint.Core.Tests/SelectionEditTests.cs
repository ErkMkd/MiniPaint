using MiniPaint.Core.Document;
using MiniPaint.Core.Selection;
using MiniPaint.Core.Transform;

namespace MiniPaint.Core.Tests;

public class SelectionEditTests
{
    [Fact]
    public void Contains_And_Offset()
    {
        var sel = new SelectionState();
        sel.Set(10, 20, 30, 40);
        Assert.True(sel.Contains(10, 20));
        Assert.True(sel.Contains(39, 59));
        Assert.False(sel.Contains(40, 20));
        sel.Offset(5, -3);
        Assert.Equal(15, sel.X);
        Assert.Equal(17, sel.Y);
        Assert.Equal(30, sel.Width);
    }

    [Fact]
    public void Scale_Default_KeepsOppositeCorner()
    {
        var sel = new SelectionState();
        sel.Set(10, 10, 40, 40);
        var edit = new SelectionEditSession(sel);
        var nwBefore = edit.GetHandleDocPoint(TransformHandle.NW);

        var ix = edit.BeginScale(TransformHandle.SE);
        // Drag SE further out; NW should stay put
        ix.Update(10 + 80, 10 + 80, constrainProportions: false, fromCenter: false);

        var nwAfter = edit.GetHandleDocPoint(TransformHandle.NW);
        Assert.Equal(nwBefore.x, nwAfter.x, 1);
        Assert.Equal(nwBefore.y, nwAfter.y, 1);
        Assert.True(sel.Width > 40);
        Assert.True(sel.Height > 40);
    }

    [Fact]
    public void Scale_Alt_KeepsCenter()
    {
        var sel = new SelectionState();
        sel.Set(0, 0, 40, 40);
        double cx0 = 20, cy0 = 20;
        var edit = new SelectionEditSession(sel);
        var ix = edit.BeginScale(TransformHandle.SE);
        ix.Update(60, 60, constrainProportions: false, fromCenter: true);

        double cx1 = sel.X + sel.Width / 2.0;
        double cy1 = sel.Y + sel.Height / 2.0;
        Assert.Equal(cx0, cx1, 1);
        Assert.Equal(cy0, cy1, 1);
        Assert.True(sel.Width > 40);
    }

    [Fact]
    public void Scale_Shift_KeepsAspect()
    {
        var sel = new SelectionState();
        sel.Set(0, 0, 40, 20);
        var edit = new SelectionEditSession(sel);
        var ix = edit.BeginScale(TransformHandle.SE);
        ix.Update(100, 30, constrainProportions: true, fromCenter: true);
        Assert.Equal(sel.Width / (double)sel.Height, 2.0, 1);
    }

    [Fact]
    public void Workspace_SelectionEdit_CancelRestores()
    {
        var doc = PaintDocument.CreateBlank("t", 100, 100);
        using var ws = new DocumentWorkspace(doc);
        ws.Selection.Set(5, 5, 20, 20);
        Assert.True(ws.BeginSelectionEdit());
        ws.Selection.Set(1, 1, 50, 50);
        ws.CancelSelectionEdit();
        Assert.False(ws.IsEditingSelection);
        Assert.Equal(5, ws.Selection.X);
        Assert.Equal(20, ws.Selection.Width);
    }

    [Fact]
    public void Workspace_BeginSelectionEdit_RequiresSelection()
    {
        var doc = PaintDocument.CreateBlank("t", 50, 50);
        using var ws = new DocumentWorkspace(doc);
        Assert.False(ws.BeginSelectionEdit());
    }
}
