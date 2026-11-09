using Microsoft.ML.OnnxRuntime.Tensors;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Selection;
using MiniPaint.Inpainting;

namespace MiniPaint.Inpainting.Tests;

public class InpaintProcessingTests
{
    private const int M = InpaintProcessing.ModelSize;

    private static SelectionState Rect(int x, int y, int w, int h)
    {
        var s = new SelectionState();
        s.Set(x, y, w, h);
        return s;
    }

    private static (int R, int G, int B, int A) At(PixelBuffer px, int x, int y)
    {
        var p = px.PixelsReadOnly;
        int i = px.Offset(x, y);
        return (p[i], p[i + 1], p[i + 2], p[i + 3]);
    }

    [Fact]
    public void Plan_SmallSelection_NativeResolutionAroundIt()
    {
        var plan = InpaintProcessing.Plan(Rect(1000, 700, 50, 40), 2000, 1500)!;

        Assert.Equal(new PixelRect(769, 464, M, M), plan.Crop); // centered on the selection
        Assert.Equal((M, M), (plan.ModelWidth, plan.ModelHeight));
        Assert.Equal(InpaintProcessing.MinExpand, plan.Expand);
        Assert.Equal(new PixelRect(996, 696, 58, 48), plan.Region); // grown by Expand + 1
    }

    [Fact]
    public void Plan_NearTheCorner_ShiftsTheCropInside()
    {
        var plan = InpaintProcessing.Plan(Rect(100, 100, 50, 40), 2000, 1500)!;

        Assert.Equal(new PixelRect(0, 0, M, M), plan.Crop);
    }

    [Fact]
    public void Plan_LargeSelection_ScalesDownWithMoreExpansion()
    {
        var plan = InpaintProcessing.Plan(Rect(1000, 500, 800, 600), 3000, 2000)!;

        Assert.Equal(new PixelRect(600, 0, 1600, 1600), plan.Crop); // twice the longest side
        Assert.Equal((M, M), (plan.ModelWidth, plan.ModelHeight));
        Assert.Equal(7, plan.Expand); // 2 model pixels at scale 0.32
        Assert.Equal(new PixelRect(992, 492, 816, 616), plan.Region);
    }

    [Fact]
    public void Plan_SmallDocument_UsesTheWholeImageKeepingItsAspect()
    {
        var plan = InpaintProcessing.Plan(Rect(100, 50, 40, 30), 300, 200)!;

        Assert.Equal(new PixelRect(0, 0, 300, 200), plan.Crop);
        Assert.Equal((M, 341), (plan.ModelWidth, plan.ModelHeight));
    }

    [Fact]
    public void Plan_NothingInsideTheDocument_IsNull()
    {
        Assert.Null(InpaintProcessing.Plan(new SelectionState(), 100, 100));
        Assert.Null(InpaintProcessing.Plan(Rect(-50, -50, 10, 10), 100, 100));
    }

    [Fact]
    public void HoleMask_AnyCoverageIsAHole()
    {
        var s = new SelectionState();
        s.SetMask(10, 10, 3, 1, [1, 128, 0], trim: false);

        var hole = InpaintProcessing.HoleMask(s, new PixelRect(9, 10, 5, 1));

        Assert.Equal(new byte[] { 0, 1, 1, 0, 0 }, hole);
    }

    [Fact]
    public void Grow_ThenEdgeWeights_FadeOnlyAcrossTheGrownBand()
    {
        const int W = 21;
        var hole = new byte[W * W];
        hole[10 * W + 10] = 1;

        var grown = InpaintProcessing.Grow(hole, W, W, 3);
        var weights = InpaintProcessing.EdgeWeights(grown, W, W, 3);

        Assert.Equal(1, grown[10 * W + 13]);
        Assert.Equal(0, grown[10 * W + 14]);
        Assert.Equal(255, weights[10 * W + 10]); // the original hole is fully replaced
        Assert.InRange(weights[10 * W + 13], 1, 254);
        Assert.Equal(0, weights[10 * W + 14]);
    }

    [Fact]
    public void ModelMask_ExactHalf_MapsEachPixelToItsModelPixel()
    {
        var plan = new InpaintPlan(new PixelRect(0, 0, 1024, 1024), new PixelRect(101, 201, 5, 3), 3, M, M);
        var hole = new byte[5 * 3];
        hole[1 * 5 + 2] = 1; // crop (103, 202) → model (51, 101)

        var mask = InpaintProcessing.ModelMask(hole, plan);

        Assert.Equal(1, mask[101 * M + 51]);
        Assert.Equal(1, mask.Count(v => v != 0));
    }

    [Fact]
    public void ModelMask_NoHolePixelIsLost()
    {
        var plan = new InpaintPlan(new PixelRect(40, 30, 1000, 700), new PixelRect(40, 30, 1000, 700), 3, M, 358);
        var rnd = new Random(7);
        var hole = new byte[1000 * 700];
        var points = Enumerable.Range(0, 200).Select(_ => (X: rnd.Next(1000), Y: rnd.Next(700))).ToList();
        foreach (var (x, y) in points) hole[y * 1000 + x] = 1;

        var mask = InpaintProcessing.ModelMask(hole, plan);

        foreach (var (x, y) in points)
        {
            int mx = (int)((x + 0.5) * M / 1000);
            int my = (int)((y + 0.5) * 358 / 700);
            Assert.Equal(1, mask[my * M + mx]);
        }
    }

    [Theory]
    [InlineData(2, 5, 2)]
    [InlineData(-1, 5, 0)]
    [InlineData(-2, 5, 1)]
    [InlineData(5, 5, 4)]
    [InlineData(6, 5, 3)]
    [InlineData(10, 5, 0)]
    public void Mirror_ReflectsAtTheEdges(int index, int length, int expected) =>
        Assert.Equal(expected, InpaintProcessing.Mirror(index, length));

    [Fact]
    public void AreaDownscale_AveragesTheCoveredPixels()
    {
        using var checker = new PixelBuffer(4, 2);
        checker.FillRect(0, 0, 4, 2, 0, 0, 0, 255);
        checker.FillRect(1, 0, 1, 1, 255, 255, 255, 255);
        checker.FillRect(0, 1, 1, 1, 255, 255, 255, 255);
        checker.FillRect(2, 0, 2, 2, 200, 100, 50, 255);

        using var small = InpaintProcessing.AreaDownscale(checker, 2, 1);

        Assert.Equal((128, 128, 128, 255), At(small, 0, 0));
        Assert.Equal((200, 100, 50, 255), At(small, 1, 0));
    }

    [Fact]
    public void CreateRequest_CopiesTheLayerUnderTheCrop()
    {
        using var layer = new PixelBuffer(50, 50);
        layer.Clear(255, 0, 0, 255);
        var selection = Rect(30, 20, 10, 10);

        using var request = InpaintProcessing.CreateRequest(layer, 20, 10, selection, 200, 100)!;
        selection.Clear();

        Assert.Equal(new PixelRect(0, 0, 200, 100), request.Plan.Crop);
        Assert.Equal((255, 0, 0, 255), At(request.Context, 25, 15)); // inside the layer
        Assert.Equal((0, 0, 0, 0), At(request.Context, 5, 5));       // outside: transparent
        Assert.True(request.Selection.HasSelection);                 // its own copy
    }

    [Fact]
    public void Inpaint_FakeModel_FillsTheHoleAndLeavesTheRest()
    {
        using var layer = new PixelBuffer(600, 400);
        layer.Clear(0, 0, 255, 255);
        var selection = Rect(250, 150, 60, 40);
        using var request = InpaintProcessing.CreateRequest(layer, 0, 0, selection, 600, 400)!;
        var crop = request.Plan.Crop; // 512 × 400 at (24, 0), scale 1

        float[] RedModel(DenseTensor<float> image, DenseTensor<float> mask)
        {
            Assert.Equal(1f, mask[0, 0, 170, 280 - crop.X]);  // hole
            Assert.Equal(0f, image[0, 2, 170, 280 - crop.X]); // blanked
            Assert.Equal(0f, mask[0, 0, 10, 10]);
            Assert.Equal(1f, image[0, 2, 10, 10]);           // blue context
            Assert.Equal(mask[0, 0, 399, 10], mask[0, 0, 400, 10]); // mirrored padding
            var output = new float[3 * M * M];
            Array.Fill(output, 255f, 0, M * M);
            return output;
        }

        using var result = InpaintProcessing.Inpaint(request, RedModel)!;
        PixelOps.BlendLayer(layer, result.Pixels, result.Region.X, result.Region.Y, 1f);

        Assert.Equal((255, 0, 0, 255), At(layer, 250, 150));
        Assert.Equal((255, 0, 0, 255), At(layer, 280, 170));
        var edge = At(layer, 248, 170); // in the grown band: mixed
        Assert.True(edge.R > 0 && edge.B > 0, $"{edge}");
        Assert.Equal((0, 0, 255, 255), At(layer, 246, 170)); // beyond the band: untouched
    }

    [Fact]
    public void Inpaint_NothingLeftAround_ReturnsNullWithoutRunningTheModel()
    {
        using var layer = new PixelBuffer(100, 80);
        layer.Clear(10, 20, 30, 255);
        using var request = InpaintProcessing.CreateRequest(layer, 0, 0, Rect(0, 0, 100, 80), 100, 80)!;

        var result = InpaintProcessing.Inpaint(request, (_, _) => throw new InvalidOperationException("model called"));

        Assert.Null(result);
    }
}
