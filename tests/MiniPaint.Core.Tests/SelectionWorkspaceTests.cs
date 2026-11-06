using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Selection;

namespace MiniPaint.Core.Tests;

public class SelectionWorkspaceTests
{
    private static DocumentWorkspace FilledWorkspace(int w, int h)
    {
        var doc = PaintDocument.CreateBlank("t", w, h, transparent: true);
        doc.Layers[0].Pixels.FillRect(0, 0, w, h, 200, 10, 10, 255);
        doc.IsDirty = false;
        return new DocumentWorkspace(doc);
    }

    private static void SelectFrame(SelectionState s)
    {
        s.Set(0, 0, 4, 4);
        var hole = new SelectionState();
        hole.Set(1, 1, 2, 2);
        s.Combine(hole, SelectionCombineMode.Subtract);
    }

    private static byte AlphaAt(PixelBuffer px, int x, int y) => px.PixelsReadOnly[px.Offset(x, y) + 3];

    [Fact]
    public void MaskedCopy_KeepsOnlySelectedPixels()
    {
        using var ws = FilledWorkspace(4, 4);
        SelectFrame(ws.Selection);

        using var copied = SelectionOps.CopyFromActiveLayer(ws.Document, ws.Selection);

        Assert.NotNull(copied);
        Assert.Equal(255, AlphaAt(copied!, 0, 0));
        Assert.Equal(0, AlphaAt(copied!, 1, 1));
    }

    [Fact]
    public void MaskedCut_ClearsOnlySelectedPixels()
    {
        using var ws = FilledWorkspace(4, 4);
        SelectFrame(ws.Selection);

        using var lifted = SelectionOps.CutFromActiveLayer(ws.Document, ws.Selection);

        var layer = ws.Document.Layers[0].Pixels;
        Assert.Equal(0, AlphaAt(layer, 0, 0));
        Assert.Equal(255, AlphaAt(layer, 1, 1));
    }

    [Fact]
    public void SoftCut_SplitsAlphaBetweenCopyAndLayer()
    {
        using var ws = FilledWorkspace(1, 1);
        ws.Selection.SetMask(0, 0, 1, 1, [128]);

        using var lifted = SelectionOps.CutFromActiveLayer(ws.Document, ws.Selection);

        Assert.Equal(128, AlphaAt(lifted!, 0, 0));
        Assert.Equal(127, AlphaAt(ws.Document.Layers[0].Pixels, 0, 0));
    }

    [Fact]
    public void SelectionChanges_AreUndoable_AndDoNotDirtyDocument()
    {
        using var ws = FilledWorkspace(8, 8);

        ws.SelectAll();
        Assert.True(ws.Selection.HasSelection);
        ws.Deselect();
        Assert.False(ws.Selection.HasSelection);

        ws.History.Undo();
        Assert.True(ws.Selection.HasSelection);
        Assert.Equal((0, 0, 8, 8), (ws.Selection.X, ws.Selection.Y, ws.Selection.Width, ws.Selection.Height));
        ws.History.Undo();
        Assert.False(ws.Selection.HasSelection);
        ws.History.Redo();
        Assert.True(ws.Selection.HasSelection);

        Assert.False(ws.Document.IsDirty);
    }

    [Fact]
    public void Deselect_WithoutSelection_RecordsNothing()
    {
        using var ws = FilledWorkspace(4, 4);
        ws.Deselect();
        Assert.False(ws.History.CanUndo);
    }

    [Fact]
    public void InvertSelection_IsUndoable()
    {
        using var ws = FilledWorkspace(4, 4);
        ws.Selection.Set(1, 1, 2, 2);

        ws.InvertSelection();
        Assert.Equal(0, ws.Selection.CoverageAt(1, 1));
        Assert.Equal(255, ws.Selection.CoverageAt(0, 0));

        ws.History.Undo();
        Assert.True(ws.Selection.IsRectangular);
        Assert.Equal((1, 1, 2, 2), (ws.Selection.X, ws.Selection.Y, ws.Selection.Width, ws.Selection.Height));
    }

    [Fact]
    public void FreeTransform_MaskedSelection_KeepsShape_AndUndoRestoresBoth()
    {
        using var ws = FilledWorkspace(8, 8);
        SelectFrame(ws.Selection);

        Assert.True(ws.BeginTransform());
        Assert.True(ws.IsSelectionTransform);
        ws.ActiveTransform!.TranslateX = 4;
        ws.ActiveTransform.TranslateY = 4;
        ws.CommitTransform();

        // Marquee follows the moved frame, hole included.
        Assert.Equal(255, ws.Selection.CoverageAt(4, 4));
        Assert.Equal(0, ws.Selection.CoverageAt(5, 5));
        var layer = ws.Document.Layers[0].Pixels;
        Assert.Equal(0, AlphaAt(layer, 0, 0));     // frame lifted from here
        Assert.Equal(255, AlphaAt(layer, 1, 1));   // hole pixels never moved

        ws.History.Undo();
        Assert.Equal(255, AlphaAt(ws.Document.Layers[0].Pixels, 0, 0));
        Assert.Equal(255, ws.Selection.CoverageAt(0, 0));
        Assert.Equal(0, ws.Selection.CoverageAt(1, 1));
    }

    [Fact]
    public void FreeTransform_Cancel_RestoresMaskedSelection()
    {
        using var ws = FilledWorkspace(8, 8);
        SelectFrame(ws.Selection);

        ws.BeginTransform();
        ws.ActiveTransform!.TranslateX = 3;
        ws.RefreshTransformPreview();
        ws.CancelTransform();

        Assert.False(ws.Selection.IsRectangular);
        Assert.Equal(0, ws.Selection.CoverageAt(1, 1));
        Assert.Equal(255, AlphaAt(ws.Document.Layers[0].Pixels, 0, 0));
    }
}
