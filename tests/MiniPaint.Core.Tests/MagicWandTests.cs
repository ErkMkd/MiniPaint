using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Selection;

namespace MiniPaint.Core.Tests;

public class MagicWandTests
{
    private static readonly MagicWandOptions Exact = new(Tolerance: 0);

    /// <summary>8×8 white image with two separate 2×2 red squares.</summary>
    private static PixelBuffer TwoRedSquares()
    {
        var px = new PixelBuffer(8, 8);
        px.FillRect(0, 0, 8, 8, 255, 255, 255, 255);
        px.FillRect(1, 1, 2, 2, 255, 0, 0, 255);
        px.FillRect(5, 5, 2, 2, 255, 0, 0, 255);
        return px;
    }

    [Fact]
    public void Contiguous_SelectsOnlyConnectedRegion()
    {
        using var px = TwoRedSquares();

        var s = MagicWand.Select(px, 1, 1, Exact);

        Assert.True(s.IsRectangular);
        Assert.Equal((1, 1, 2, 2), (s.X, s.Y, s.Width, s.Height));
    }

    [Fact]
    public void NonContiguous_SelectsColorEverywhere()
    {
        using var px = TwoRedSquares();

        var s = MagicWand.Select(px, 1, 1, Exact with { Contiguous = false });

        Assert.Equal((1, 1, 6, 6), (s.X, s.Y, s.Width, s.Height));
        Assert.Equal(255, s.CoverageAt(6, 6));
        Assert.Equal(0, s.CoverageAt(4, 4));
    }

    [Fact]
    public void Contiguous_IsFourConnected()
    {
        using var px = new PixelBuffer(3, 3);
        px.FillRect(0, 0, 3, 3, 255, 255, 255, 255);
        px.FillRect(0, 0, 1, 1, 0, 0, 0, 255);
        px.FillRect(1, 1, 1, 1, 0, 0, 0, 255); // diagonal neighbour only

        var s = MagicWand.Select(px, 0, 0, Exact);

        Assert.Equal((0, 0, 1, 1), (s.X, s.Y, s.Width, s.Height));
    }

    [Fact]
    public void Contiguous_FollowsWindingShape()
    {
        // U shape: the fill has to go down, across and back up.
        using var px = new PixelBuffer(5, 5);
        px.FillRect(0, 0, 5, 5, 255, 255, 255, 255);
        px.FillRect(0, 0, 1, 5, 0, 0, 0, 255);
        px.FillRect(0, 4, 5, 1, 0, 0, 0, 255);
        px.FillRect(4, 0, 1, 5, 0, 0, 0, 255);

        var s = MagicWand.Select(px, 0, 0, Exact);

        Assert.Equal(255, s.CoverageAt(4, 0));
        Assert.Equal(0, s.CoverageAt(2, 2));
    }

    [Fact]
    public void Tolerance_IsMaxChannelDifference()
    {
        using var px = new PixelBuffer(2, 1);
        px.FillRect(0, 0, 1, 1, 100, 100, 100, 255);
        px.FillRect(1, 0, 1, 1, 110, 100, 100, 255);

        Assert.Equal(1, MagicWand.Select(px, 0, 0, new MagicWandOptions(Tolerance: 9)).Width);
        Assert.Equal(2, MagicWand.Select(px, 0, 0, new MagicWandOptions(Tolerance: 10)).Width);
    }

    [Fact]
    public void TransparentPixels_MatchRegardlessOfHiddenColor()
    {
        using var px = new PixelBuffer(3, 1);
        px.FillRect(0, 0, 1, 1, 0, 0, 0, 0);
        px.FillRect(1, 0, 1, 1, 200, 50, 10, 0);   // transparent with leftover RGB
        px.FillRect(2, 0, 1, 1, 0, 0, 0, 255);     // opaque black

        var s = MagicWand.Select(px, 0, 0, Exact);

        Assert.Equal((0, 0, 2, 1), (s.X, s.Y, s.Width, s.Height));
    }

    [Fact]
    public void SeedOutsideImage_SelectsNothing()
    {
        using var px = TwoRedSquares();
        Assert.False(MagicWand.Select(px, -1, 3, Exact).HasSelection);
        Assert.False(MagicWand.Select(px, 8, 3, Exact).HasSelection);
    }

    [Fact]
    public void AntiAlias_SoftensEdges_KeepsInteriorAndImageBorder()
    {
        using var px = new PixelBuffer(10, 10);
        px.FillRect(0, 0, 10, 10, 255, 255, 255, 255);
        px.FillRect(0, 0, 5, 10, 0, 0, 255, 255); // left half blue, touching the image border

        var s = MagicWand.Select(px, 1, 5, Exact with { AntiAlias = true });

        Assert.Equal(255, s.CoverageAt(0, 5));            // image border stays fully selected
        Assert.Equal(255, s.CoverageAt(2, 5));            // interior
        Assert.InRange(s.CoverageAt(4, 5), 129, 254);     // inside edge: partial, still ≥ 50%
        Assert.InRange(s.CoverageAt(5, 5), 1, 127);       // outside edge: partial, < 50%
        Assert.Equal(0, s.CoverageAt(7, 5));
    }

    [Fact]
    public void Workspace_SamplesVisibleImageOrActiveLayer()
    {
        var doc = PaintDocument.CreateBlank("t", 8, 8, transparent: false);
        doc.Layers[0].Pixels.FillRect(0, 0, 8, 8, 255, 0, 0, 255);
        var top = new PixelBuffer(2, 2);
        top.FillRect(0, 0, 2, 2, 0, 0, 255, 255);
        doc.AddLayer(new Layer("Top", top, 3, 3));
        doc.ActiveLayerId = doc.Layers[0].Id; // bottom (all red) is active
        using var ws = new DocumentWorkspace(doc);

        ws.MagicWandSelect(4, 4, Exact, SelectionCombineMode.Replace);
        Assert.Equal((3, 3, 2, 2), (ws.Selection.X, ws.Selection.Y, ws.Selection.Width, ws.Selection.Height));

        ws.MagicWandSelect(4, 4, Exact with { SampleVisibleImage = false }, SelectionCombineMode.Replace);
        Assert.Equal((0, 0, 8, 8), (ws.Selection.X, ws.Selection.Y, ws.Selection.Width, ws.Selection.Height));

        ws.History.Undo();
        Assert.Equal((3, 3, 2, 2), (ws.Selection.X, ws.Selection.Y, ws.Selection.Width, ws.Selection.Height));
    }

    [Fact]
    public void Workspace_CombinesWithCurrentSelection()
    {
        var doc = PaintDocument.CreateBlank("t", 8, 8, transparent: false);
        using var ws = new DocumentWorkspace(doc);
        doc.Layers[0].Pixels.FillRect(0, 0, 8, 8, 255, 255, 255, 255);
        doc.Layers[0].Pixels.FillRect(1, 1, 2, 2, 255, 0, 0, 255);
        doc.Layers[0].Pixels.FillRect(5, 5, 2, 2, 255, 0, 0, 255);
        ws.InvalidateComposite();

        ws.MagicWandSelect(1, 1, Exact, SelectionCombineMode.Replace);
        ws.MagicWandSelect(5, 5, Exact, SelectionCombineMode.Add);

        Assert.Equal(255, ws.Selection.CoverageAt(1, 1));
        Assert.Equal(255, ws.Selection.CoverageAt(6, 6));
        Assert.Equal(0, ws.Selection.CoverageAt(4, 4));

        ws.MagicWandSelect(5, 5, Exact, SelectionCombineMode.Subtract);
        Assert.Equal((1, 1, 2, 2), (ws.Selection.X, ws.Selection.Y, ws.Selection.Width, ws.Selection.Height));
    }
}
