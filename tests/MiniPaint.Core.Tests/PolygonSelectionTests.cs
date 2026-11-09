using MiniPaint.Core.Selection;

namespace MiniPaint.Core.Tests;

public class PolygonSelectionTests
{
    private static int Count(SelectionState s, int w, int h)
    {
        int n = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (s.CoverageAt(x, y) >= 128) n++;
        return n;
    }

    [Fact]
    public void Square_OnPixelEdges_IsExact()
    {
        var s = PolygonSelection.Rasterize([(2, 3), (7, 3), (7, 8), (2, 8)], antiAlias: true, 20, 20);

        Assert.Equal((2, 3, 5, 5), (s.X, s.Y, s.Width, s.Height));
        Assert.True(s.IsRectangular); // fully covered pixels only
    }

    [Fact]
    public void Triangle_CoversAboutItsArea()
    {
        var s = PolygonSelection.Rasterize([(0, 0), (40, 0), (0, 40)], antiAlias: true, 50, 50);

        double area = 0;
        for (int y = 0; y < 50; y++)
            for (int x = 0; x < 50; x++)
                area += s.CoverageAt(x, y) / 255.0;
        Assert.InRange(area, 790, 810); // 40 × 40 / 2
        Assert.Equal(255, s.CoverageAt(5, 5));
        Assert.Equal(0, s.CoverageAt(30, 30));
    }

    [Fact]
    public void AntiAlias_GivesTheDiagonalEdgeHalfCoverage_WithoutItPixelsAreAllOrNothing()
    {
        (double, double)[] triangle = [(0, 0), (10, 0), (0, 10)];

        var soft = PolygonSelection.Rasterize(triangle, antiAlias: true, 10, 10);
        var hard = PolygonSelection.Rasterize(triangle, antiAlias: false, 10, 10);

        Assert.InRange(soft.CoverageAt(4, 5), 100, 155); // the edge crosses this pixel's diagonal
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 10; x++)
                Assert.True(hard.CoverageAt(x, y) is 0 or 255);
    }

    [Fact]
    public void Concave_LShape_LeavesTheNotchOut()
    {
        var s = PolygonSelection.Rasterize([(0, 0), (10, 0), (10, 4), (4, 4), (4, 10), (0, 10)], antiAlias: false, 20, 20);

        Assert.Equal(255, s.CoverageAt(8, 2));
        Assert.Equal(255, s.CoverageAt(2, 8));
        Assert.Equal(0, s.CoverageAt(8, 8));
        Assert.Equal(10 * 4 + 6 * 4, Count(s, 20, 20));
    }

    [Fact]
    public void ClippedToTheDocument()
    {
        var s = PolygonSelection.Rasterize([(-10, -10), (5, -10), (5, 5), (-10, 5)], antiAlias: true, 20, 20);

        Assert.Equal((0, 0, 5, 5), (s.X, s.Y, s.Width, s.Height));
    }

    [Fact]
    public void TooFewCornersOrOutside_IsEmpty()
    {
        Assert.False(PolygonSelection.Rasterize([(0, 0), (5, 5)], true, 10, 10).HasSelection);
        Assert.False(PolygonSelection.Rasterize([(20, 20), (30, 20), (30, 30)], true, 10, 10).HasSelection);
    }

    [Fact]
    public void SelfCrossingLoop_SelectsEverythingItEncloses()
    {
        // A square traced twice the same way (winding 2): non-zero keeps it selected.
        var s = PolygonSelection.Rasterize(
            [(0, 0), (10, 0), (10, 10), (0, 10), (0, 0), (10, 0), (10, 10), (0, 10)], antiAlias: false, 20, 20);

        Assert.Equal(100, Count(s, 20, 20));
    }

    [Theory]
    [InlineData(10, 1, 10, 0)]   // nearly horizontal → horizontal
    [InlineData(7, 8, 7.5, 7.5)] // nearly diagonal → 45°
    [InlineData(-1, -9, 0, -9)]  // nearly vertical → vertical (length kept)
    public void Constrain45_SnapsTheSide(double tx, double ty, double ex, double ey)
    {
        var (x, y) = PolygonSelection.Constrain45((0, 0), (tx, ty));
        double length = Math.Sqrt(tx * tx + ty * ty);
        Assert.Equal(length, Math.Sqrt(x * x + y * y), 6);
        Assert.Equal(Math.Atan2(ey, ex), Math.Atan2(y, x), 6);
    }
}
