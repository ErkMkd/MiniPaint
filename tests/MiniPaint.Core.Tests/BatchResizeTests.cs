using MiniPaint.Core.Document;

namespace MiniPaint.Core.Tests;

public class BatchResizeTests
{
    [Fact]
    public void Percentage_ScalesBothSides()
    {
        var size = BatchResize.ComputeTargetSize(1920, 1080, new BatchResizeOptions(BatchResizeMode.Percentage, Percent: 50));
        Assert.Equal((960, 540), size);
    }

    [Fact]
    public void Percentage_NeverBelowOnePixel()
    {
        var size = BatchResize.ComputeTargetSize(10, 3, new BatchResizeOptions(BatchResizeMode.Percentage, Percent: 1));
        Assert.Equal((1, 1), size);
    }

    [Theory]
    [InlineData(1920, 1080, 800, 800, 800, 450)] // landscape → width bound
    [InlineData(1080, 1920, 800, 800, 450, 800)] // portrait → height bound
    [InlineData(400, 300, 800, 800, 800, 600)]   // smaller image is enlarged
    public void FitWithin_KeepsProportions(int w, int h, int boxW, int boxH, int expectedW, int expectedH)
    {
        var options = new BatchResizeOptions(BatchResizeMode.FitWithin, Width: boxW, Height: boxH);
        Assert.Equal((expectedW, expectedH), BatchResize.ComputeTargetSize(w, h, options));
    }

    [Fact]
    public void FitWithin_DoNotEnlarge_KeepsSmallImages()
    {
        var options = new BatchResizeOptions(BatchResizeMode.FitWithin, Width: 800, Height: 800, DoNotEnlarge: true);
        Assert.Equal((400, 300), BatchResize.ComputeTargetSize(400, 300, options));
        Assert.Equal((800, 450), BatchResize.ComputeTargetSize(1920, 1080, options));
    }

    [Fact]
    public void ExactSize_IgnoresSourceProportions()
    {
        var options = new BatchResizeOptions(BatchResizeMode.ExactSize, Width: 256, Height: 256);
        Assert.Equal((256, 256), BatchResize.ComputeTargetSize(1920, 1080, options));
    }

    [Fact]
    public void InvalidOptions_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BatchResize.ComputeTargetSize(100, 100, new BatchResizeOptions(BatchResizeMode.Percentage, Percent: 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BatchResize.ComputeTargetSize(100, 100, new BatchResizeOptions(BatchResizeMode.FitWithin, Width: 0, Height: 50)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BatchResize.ComputeTargetSize(100, 100, new BatchResizeOptions(BatchResizeMode.ExactSize, Width: 50, Height: -1)));
    }
}
