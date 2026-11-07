using MiniPaint.Core.Imaging;
using MiniPaint.Core.Selection;
using MiniPaint.Segmentation;

namespace MiniPaint.Segmentation.Tests;

/// <summary>Skipped unless scripts\Get-Models.ps1 has downloaded the MobileSAM models.</summary>
public sealed class RequiresSamModelsFactAttribute : FactAttribute
{
    public static string ModelDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Models", "MobileSAM");

    public RequiresSamModelsFactAttribute()
    {
        if (!SamSegmenter.ModelsExist(ModelDirectory))
            Skip = "MobileSAM models not downloaded (run scripts\\Get-Models.ps1).";
    }
}

/// <summary>End-to-end: real MobileSAM on a synthetic scene with known object shapes.</summary>
public class SamModelTests
{
    private const int W = 640, H = 480;

    /// <summary>Gray background, red disc (r 90) at (200, 240), blue square 140 px at (400..540, 170..310).</summary>
    private static PixelBuffer Scene()
    {
        var px = new PixelBuffer(W, H);
        px.FillRect(0, 0, W, H, 170, 170, 165, 255);
        var p = px.Pixels;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if ((x - 200) * (x - 200) + (y - 240) * (y - 240) <= 90 * 90)
                {
                    int i = (y * W + x) * 4;
                    p[i] = 210; p[i + 1] = 30; p[i + 2] = 40;
                }
            }
        px.FillRect(400, 170, 140, 140, 30, 60, 200, 255);
        return px;
    }

    /// <summary>Intersection over union of the selection (coverage ≥ 50%) with a ground-truth shape.</summary>
    private static double Iou(SelectionState s, Func<int, int, bool> truth)
    {
        int inter = 0, union = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                bool a = s.CoverageAt(x, y) >= 128;
                bool b = truth(x, y);
                if (a && b) inter++;
                if (a || b) union++;
            }
        return union == 0 ? 0 : inter / (double)union;
    }

    [RequiresSamModelsFact]
    public void ClickOnDisc_SelectsTheDisc()
    {
        using var scene = Scene();
        using var sam = SamSegmenter.Load(RequiresSamModelsFactAttribute.ModelDirectory);
        var embedding = sam.Encode(SamProcessing.ToEncoderInput(scene));

        var s = sam.Segment(embedding, SamPrompt.Point(200, 240), softEdges: false);

        double iou = Iou(s, (x, y) => (x - 200) * (x - 200) + (y - 240) * (y - 240) <= 90 * 90);
        Assert.True(iou > 0.9, $"IoU {iou:0.000}");
    }

    [RequiresSamModelsFact]
    public void BoxAroundSquare_SelectsTheSquare_WithSoftEdges()
    {
        using var scene = Scene();
        using var sam = SamSegmenter.Load(RequiresSamModelsFactAttribute.ModelDirectory);
        var embedding = sam.Encode(SamProcessing.ToEncoderInput(scene));

        var s = sam.Segment(embedding, SamPrompt.Box(385, 155, 555, 325), softEdges: true);

        double iou = Iou(s, (x, y) => x >= 400 && x < 540 && y >= 170 && y < 310);
        Assert.True(iou > 0.9, $"IoU {iou:0.000}");
        Assert.False(s.IsRectangular); // soft edges: partial coverage along the border
    }
}
