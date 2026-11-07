using MiniPaint.Core.Imaging;
using MiniPaint.Segmentation;

namespace MiniPaint.Segmentation.Tests;

public class SamProcessingTests
{
    [Theory]
    [InlineData(800, 450, 1024, 576)]
    [InlineData(450, 800, 576, 1024)]
    [InlineData(4000, 3000, 1024, 768)]
    [InlineData(10, 10, 1024, 1024)]
    public void ResizedSize_LongestSideIs1024(int w, int h, int rw, int rh)
    {
        Assert.Equal((rw, rh), SamProcessing.ResizedSize(w, h));
    }

    [Fact]
    public void ToEncoderInput_IsHwcRgb_AndTransparencyBecomesGray()
    {
        using var image = new PixelBuffer(8, 4);
        image.FillRect(0, 0, 8, 4, 10, 20, 30, 255);
        var input = SamProcessing.ToEncoderInput(image);

        Assert.Equal([512, 1024, 3], input.Pixels.Dimensions.ToArray());
        Assert.Equal((8, 4, 1024, 512), (input.Width, input.Height, input.ResizedWidth, input.ResizedHeight));
        Assert.Equal(10f, input.Pixels[100, 100, 0], 1);
        Assert.Equal(30f, input.Pixels[100, 100, 2], 1);

        using var clear = new PixelBuffer(8, 4);
        var gray = SamProcessing.ToEncoderInput(clear);
        Assert.Equal(SamProcessing.Background, gray.Pixels[10, 10, 1], 1);
    }

    [Fact]
    public void EncodePrompt_Click_AddsPaddingPoint()
    {
        var (coords, labels) = SamProcessing.EncodePrompt(SamPrompt.Point(400, 225), 800, 450, 1024, 576);

        Assert.Equal([512f, 288f, 0f, 0f], coords);
        Assert.Equal([1f, -1f], labels);
    }

    [Fact]
    public void EncodePrompt_Box_UsesCornerLabels_AndNormalizedCorners()
    {
        var box = SamPrompt.Box(600, 300, 200, 100); // dragged up-left
        var (coords, labels) = SamProcessing.EncodePrompt(box, 800, 400, 1024, 512);

        Assert.Equal([256f, 128f, 768f, 384f], coords);
        Assert.Equal([2f, 3f], labels);
    }

    [Fact]
    public void ChooseMask_BoxTakesSingleAnswer_ClickTakesBestAlternative()
    {
        float[] iou = [0.99f, 0.70f, 0.95f, 0.80f];
        Assert.Equal(0, SamProcessing.ChooseMask(iou, isBox: true));
        Assert.Equal(2, SamProcessing.ChooseMask(iou, isBox: false));
    }

    [Fact]
    public void LogitsToCoverage_Hard_ThresholdsAtZero()
    {
        float[] logits = [-5, 5, -5, 5]; // 2×2: left outside, right inside
        var cov = SamProcessing.LogitsToCoverage(logits, 2, 2, 4, 4, softEdges: false);

        Assert.Equal(0, cov[0]);
        Assert.Equal(0, cov[1]);   // left of the interpolated boundary
        Assert.Equal(255, cov[2]);
        Assert.Equal(255, cov[3]);
    }

    [Fact]
    public void LogitsToCoverage_Soft_IsPartialNearTheEdgeOnly()
    {
        float[] logits = [-20, 20, -20, 20];
        var cov = SamProcessing.LogitsToCoverage(logits, 2, 2, 8, 1, softEdges: true);

        Assert.Equal(0, cov[0]);
        Assert.Equal(255, cov[7]);
        Assert.InRange(cov[3], 1, 254); // pixel just left of the middle: soft
        Assert.InRange(cov[4], 1, 254);
    }
}
