using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Selection;

namespace MiniPaint.Core.Tests;

public class ColorRangeTests
{
    private static readonly SampledColor Red = new(255, 0, 0, 255);
    private static readonly SampledColor Blue = new(0, 0, 255, 255);

    private static ColorRangeOptions Pick(int fuzziness, params SampledColor[] include) =>
        new(include, [], fuzziness);

    /// <summary>4×1: red, red+20 green, blue, white.</summary>
    private static PixelBuffer Strip()
    {
        var px = new PixelBuffer(4, 1);
        px.FillRect(0, 0, 1, 1, 255, 0, 0, 255);
        px.FillRect(1, 0, 1, 1, 255, 20, 0, 255);
        px.FillRect(2, 0, 1, 1, 0, 0, 255, 255);
        px.FillRect(3, 0, 1, 1, 255, 255, 255, 255);
        return px;
    }

    [Fact]
    public void FuzzinessZero_SelectsExactColorOnly()
    {
        using var px = Strip();
        var cov = ColorRange.Coverage(px, Pick(0, Red));
        Assert.Equal([255, 0, 0, 0], cov);
    }

    [Fact]
    public void Fuzziness_FullyInsideHalf_ThenFades()
    {
        Assert.Equal(255, ColorRange.CoverageForDistance(20, 40));
        Assert.Equal(128, ColorRange.CoverageForDistance(30, 40));
        Assert.Equal(0, ColorRange.CoverageForDistance(40, 40));

        using var px = Strip();
        var cov = ColorRange.Coverage(px, Pick(40, Red)); // second pixel is 20 away: fully selected
        Assert.Equal(255, cov[1]);
        Assert.Equal(0, cov[2]);
    }

    [Fact]
    public void SeveralColors_AreUnited_NotOnlyConnected()
    {
        using var px = Strip();
        var cov = ColorRange.Coverage(px, Pick(0, Red, Blue));
        Assert.Equal([255, 0, 255, 0], cov);
    }

    [Fact]
    public void ExcludedColor_IsRemoved()
    {
        using var px = Strip();
        var options = new ColorRangeOptions([Red], [new SampledColor(255, 20, 0, 255)], 40);

        var cov = ColorRange.Coverage(px, options);

        Assert.Equal(0, cov[1]);
        Assert.Equal(0, cov[0]); // red itself is within the excluded color's fully selected range too
    }

    [Fact]
    public void NoColor_SelectsNothing()
    {
        using var px = Strip();
        Assert.False(ColorRange.Select(px, Pick(100)).HasSelection);
    }

    [Fact]
    public void TransparentSample_SelectsAllTransparentPixels()
    {
        using var px = new PixelBuffer(3, 1);
        px.FillRect(1, 0, 1, 1, 90, 10, 10, 0); // transparent with leftover RGB
        px.FillRect(2, 0, 1, 1, 0, 0, 0, 255);

        var s = ColorRange.Select(px, Pick(0, ColorRange.SampleAt(px, 0, 0)));

        Assert.Equal((0, 0, 2, 1), (s.X, s.Y, s.Width, s.Height));
    }

    [Fact]
    public void Workspace_CombinesAndUndoes()
    {
        var doc = PaintDocument.CreateBlank("t", 4, 1, transparent: false);
        using var ws = new DocumentWorkspace(doc);
        using (var strip = Strip())
            strip.PixelsReadOnly.CopyTo(doc.Layers[0].Pixels.Pixels);
        ws.InvalidateComposite();

        ws.ColorRangeSelect(Pick(0, Red), SelectionCombineMode.Replace);
        ws.ColorRangeSelect(Pick(0, Blue), SelectionCombineMode.Add);
        Assert.Equal(255, ws.Selection.CoverageAt(0, 0));
        Assert.Equal(255, ws.Selection.CoverageAt(2, 0));
        Assert.Equal(0, ws.Selection.CoverageAt(1, 0));

        ws.History.Undo();
        Assert.Equal(0, ws.Selection.CoverageAt(2, 0));
    }

    [Fact]
    public void ResampleNearest_KeepsExactColors()
    {
        using var px = Strip();
        using var big = PixelOps.ResampleNearest(px, 8, 2);
        Assert.Equal((8, 2), (big.Width, big.Height));
        var c = big.PixelsReadOnly;
        Assert.Equal(255, c[big.Offset(0, 1)]);       // red
        Assert.Equal(20, c[big.Offset(3, 0) + 1]);    // red + 20 green, not a blend
        Assert.Equal(255, c[big.Offset(5, 1) + 2]);   // blue
    }
}
