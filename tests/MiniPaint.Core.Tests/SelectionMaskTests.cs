using MiniPaint.Core.Document;
using MiniPaint.Core.Selection;
using MiniPaint.Core.Transform;

namespace MiniPaint.Core.Tests;

public class SelectionMaskTests
{
    private static SelectionState Rect(int x, int y, int w, int h)
    {
        var s = new SelectionState();
        s.Set(x, y, w, h);
        return s;
    }

    /// <summary>4×4 L: full left column pair + full bottom row pair.</summary>
    private static SelectionState LShape()
    {
        var s = Rect(0, 0, 2, 4);
        s.Combine(Rect(0, 2, 4, 2), SelectionCombineMode.Add);
        return s;
    }

    /// <summary>4×4 square with a 2×2 hole in the middle.</summary>
    private static SelectionState Frame()
    {
        var s = Rect(0, 0, 4, 4);
        s.Combine(Rect(1, 1, 2, 2), SelectionCombineMode.Subtract);
        return s;
    }

    [Fact]
    public void Add_TwoRects_MakesLShape()
    {
        var s = LShape();

        Assert.False(s.IsRectangular);
        Assert.Equal((0, 0, 4, 4), (s.X, s.Y, s.Width, s.Height));
        Assert.Equal(255, s.CoverageAt(1, 0));
        Assert.Equal(0, s.CoverageAt(3, 0));
        Assert.Equal(255, s.CoverageAt(3, 3));
    }

    [Fact]
    public void Add_ContainedRect_StaysRectangular()
    {
        var s = Rect(0, 0, 10, 10);
        s.Combine(Rect(2, 2, 3, 3), SelectionCombineMode.Add);

        Assert.True(s.IsRectangular);
        Assert.Equal((0, 0, 10, 10), (s.X, s.Y, s.Width, s.Height));
    }

    [Fact]
    public void Subtract_MakesHole_AndSubtractingAllClears()
    {
        var s = Frame();
        Assert.False(s.IsRectangular);
        Assert.Equal(255, s.CoverageAt(0, 0));
        Assert.Equal(0, s.CoverageAt(1, 1));
        Assert.False(s.Contains(2, 2));

        s.Combine(Rect(-5, -5, 20, 20), SelectionCombineMode.Subtract);
        Assert.False(s.HasSelection);
    }

    [Fact]
    public void Intersect_OverlapIsRectangle_DisjointClears()
    {
        var s = Rect(0, 0, 4, 4);
        s.Combine(Rect(2, 2, 4, 4), SelectionCombineMode.Intersect);
        Assert.True(s.IsRectangular);
        Assert.Equal((2, 2, 2, 2), (s.X, s.Y, s.Width, s.Height));

        s.Combine(Rect(10, 10, 2, 2), SelectionCombineMode.Intersect);
        Assert.False(s.HasSelection);
    }

    [Fact]
    public void Combine_SoftCoverage_UsesPhotoshopMath()
    {
        var a = new SelectionState();
        a.SetMask(0, 0, 1, 1, [128]);
        var b = new SelectionState();
        b.SetMask(0, 0, 1, 1, [128]);

        var add = a.Clone();
        add.Combine(b, SelectionCombineMode.Add);
        var sub = a.Clone();
        sub.Combine(b, SelectionCombineMode.Subtract);
        var inter = a.Clone();
        inter.Combine(b, SelectionCombineMode.Intersect);

        Assert.Equal(192, add.CoverageAt(0, 0));
        Assert.Equal(64, sub.CoverageAt(0, 0));
        Assert.Equal(64, inter.CoverageAt(0, 0));
    }

    [Fact]
    public void Invert_Twice_RestoresRectangle()
    {
        var s = Rect(1, 1, 2, 2);
        s.Invert(4, 4);
        Assert.Equal((0, 0, 4, 4), (s.X, s.Y, s.Width, s.Height));
        Assert.Equal(0, s.CoverageAt(1, 1));
        Assert.Equal(255, s.CoverageAt(0, 3));

        s.Invert(4, 4);
        Assert.True(s.IsRectangular);
        Assert.Equal((1, 1, 2, 2), (s.X, s.Y, s.Width, s.Height));
    }

    [Fact]
    public void SetMask_TrimsEmptyBorders()
    {
        var s = new SelectionState();
        s.SetMask(10, 10, 3, 3, [0, 0, 0, 0, 200, 0, 0, 0, 0]);

        Assert.Equal((11, 11, 1, 1), (s.X, s.Y, s.Width, s.Height));
        Assert.Equal(200, s.CoverageAt(11, 11));
    }

    [Fact]
    public void MoveTo_And_Clamp_KeepShape()
    {
        var s = LShape();
        s.MoveTo(-2, 5);
        Assert.Equal(255, s.CoverageAt(-1, 5));
        Assert.Equal(0, s.CoverageAt(1, 5));

        // Only the right end of the foot (x 0..1, y 7..8) is inside the document.
        s.ClampToDocument(10, 10);
        Assert.Equal((0, 7, 2, 2), (s.X, s.Y, s.Width, s.Height));
        Assert.True(s.IsRectangular);
    }

    [Fact]
    public void SetScaled_ResamplesMask()
    {
        var s = new SelectionState();
        s.SetScaled(Frame(), 0, 0, 8, 8);

        Assert.Equal((0, 0, 8, 8), (s.X, s.Y, s.Width, s.Height));
        Assert.Equal(255, s.CoverageAt(0, 0));
        Assert.Equal(255, s.CoverageAt(7, 7));
        Assert.Equal(0, s.CoverageAt(3, 3));
        Assert.Equal(0, s.CoverageAt(4, 4));
    }

    [Fact]
    public void Snapshot_RoundTrip_AndSameAs()
    {
        var s = Frame();
        var snap = s.Capture();

        s.Clear();
        Assert.False(snap.SameAs(s.Capture()));

        s.Restore(snap);
        Assert.True(snap.SameAs(s.Capture()));
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                Assert.Equal(Frame().CoverageAt(x, y), s.CoverageAt(x, y));
    }

    [Fact]
    public void Snapshot_LargeSparseMask_IsCompact()
    {
        var mask = new byte[1000 * 1000];
        mask[0] = 255;
        mask[^1] = 255;
        var s = new SelectionState();
        s.SetMask(0, 0, 1000, 1000, mask);

        var snap = s.Capture();
        var restored = new SelectionState();
        restored.Restore(snap);

        Assert.Equal(255, restored.CoverageAt(999, 999));
        Assert.Equal(0, restored.CoverageAt(500, 500));
        Assert.True(snap.SameAs(restored.Capture()));
    }

    [Fact]
    public void Contour_Rect_L_Frame_SegmentCounts()
    {
        Assert.Equal(4, SelectionContour.Build(Rect(3, 3, 5, 2)).Count);
        Assert.Equal(6, SelectionContour.Build(LShape()).Count);
        Assert.Equal(8, SelectionContour.Build(Frame()).Count);
        Assert.Empty(SelectionContour.Build(new SelectionState()));
    }

    [Fact]
    public void Contour_LShape_TracesPixelEdges()
    {
        var segments = SelectionContour.Build(LShape());

        Assert.Contains(new ContourSegment(0, 0, 2, 0), segments); // top of the vertical bar
        Assert.Contains(new ContourSegment(2, 2, 4, 2), segments); // top of the foot
        Assert.Contains(new ContourSegment(2, 0, 2, 2), segments); // inner corner
    }

    [Fact]
    public void Contour_CoarseCells_SimplifiesFragmentedSelection()
    {
        // 8×8 checkerboard of single pixels: 1 px cells trace every pixel edge, 8 px cells give one box.
        var mask = new byte[64];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                mask[y * 8 + x] = (byte)((x + y) % 2 == 0 ? 255 : 0);
        var s = new SelectionState();
        s.SetMask(10, 20, 8, 8, mask);

        // Per axis: 7 inner lines merge into one segment each, the 2 border lines give 4 each.
        Assert.Equal(30, SelectionContour.Build(s).Count);
        var coarse = SelectionContour.Build(s, cellSize: 8);
        Assert.Equal(4, coarse.Count);
        Assert.Contains(new ContourSegment(10, 20, 18, 20), coarse);
    }

    [Fact]
    public void Contour_CoarseCells_ClampPartialCellToBounds()
    {
        var s = new SelectionState();
        s.SetMask(0, 0, 5, 1, [255, 0, 255, 0, 255]); // 5 px wide: last 4-px cell is partial

        var coarse = SelectionContour.Build(s, cellSize: 4);

        Assert.All(coarse, seg => Assert.True(seg.X0 <= 5 && seg.X1 <= 5));
    }

    [Fact]
    public void ScaleBy_HalvesSelection()
    {
        var s = Rect(10, 20, 30, 40);
        s.ScaleBy(0.5, 0.5);
        Assert.Equal((5, 10, 15, 20), (s.X, s.Y, s.Width, s.Height));
    }

    [Fact]
    public void SelectionEdit_Cancel_RestoresMask()
    {
        var s = Frame();
        var edit = new SelectionEditSession(s);
        edit.BeginScale(TransformHandle.SE).Update(8, 8, constrainProportions: false, fromCenter: false);
        Assert.Equal(8, s.Width);
        Assert.False(s.IsRectangular);

        edit.RestoreOriginal();
        Assert.Equal(4, s.Width);
        Assert.Equal(0, s.CoverageAt(1, 1));
        Assert.Equal(255, s.CoverageAt(0, 0));
    }
}
