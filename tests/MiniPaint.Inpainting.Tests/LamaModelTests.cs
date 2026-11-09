using MiniPaint.Core.Imaging;
using MiniPaint.Core.Selection;
using MiniPaint.Inpainting;

namespace MiniPaint.Inpainting.Tests;

/// <summary>Skipped unless scripts\Get-Models.ps1 has downloaded the LaMa model.</summary>
public sealed class RequiresLamaModelFactAttribute : FactAttribute
{
    public static string ModelDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Models", "LaMa");

    public RequiresLamaModelFactAttribute()
    {
        if (!LamaInpainter.ModelExists(ModelDirectory))
            Skip = "LaMa model not downloaded (run scripts\\Get-Models.ps1).";
    }
}

/// <summary>Loads the model once for all the tests of the class.</summary>
public sealed class LamaFixture : IDisposable
{
    private readonly Lazy<LamaInpainter> _lama = new(() => LamaInpainter.Load(RequiresLamaModelFactAttribute.ModelDirectory));

    public LamaInpainter Lama => _lama.Value;

    public void Dispose()
    {
        if (_lama.IsValueCreated) _lama.Value.Dispose();
    }
}

/// <summary>End-to-end: real LaMa removes an object from a synthetic scene with a known background.</summary>
public class LamaModelTests(LamaFixture fixture) : IClassFixture<LamaFixture>
{
    private const int CenterX = 320, CenterY = 240, Radius = 60;

    /// <summary>Horizontal green / blue stripes, 12 px high, with a soft left-to-right shade.</summary>
    private static (byte R, byte G, byte B) Background(int x, int y)
    {
        int shade = x / 16;
        return (y / 12) % 2 == 0 ? ((byte)(50 + shade), (byte)140, (byte)80) : ((byte)(40 + shade), (byte)90, (byte)160);
    }

    private static bool InDisc(int x, int y) =>
        (x - CenterX) * (x - CenterX) + (y - CenterY) * (y - CenterY) <= Radius * Radius;

    private static PixelBuffer Scene(int width, int height)
    {
        var px = new PixelBuffer(width, height);
        var p = px.Pixels;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var (r, g, b) = InDisc(x, y) ? ((byte)220, (byte)30, (byte)40) : Background(x, y);
                int i = (y * width + x) * 4;
                p[i] = r;
                p[i + 1] = g;
                p[i + 2] = b;
                p[i + 3] = 255;
            }
        return px;
    }

    private static SelectionState DiscSelection(int width, int height)
    {
        var mask = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                mask[y * width + x] = InDisc(x, y) ? (byte)255 : (byte)0;
        var s = new SelectionState();
        s.SetMask(0, 0, width, height, mask);
        return s;
    }

    private PixelBuffer Fill(PixelBuffer scene, SelectionState selection)
    {
        using var request = InpaintProcessing.CreateRequest(scene, 0, 0, selection, scene.Width, scene.Height)!;
        using var result = fixture.Lama.Inpaint(request)!;
        var filled = scene.Clone();
        PixelOps.BlendLayer(filled, result.Pixels, result.Region.X, result.Region.Y, 1f);
        return filled;
    }

    [RequiresLamaModelFact]
    public void RemovesTheDisc_AndContinuesTheStripes()
    {
        const int W = 640, H = 480;
        using var scene = Scene(W, H);
        using var filled = Fill(scene, DiscSelection(W, H));

        var px = filled.PixelsReadOnly;
        long error = 0;
        int count = 0, reddish = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * 4;
                if (!InDisc(x, y))
                {
                    // Untouched beyond the grown band around the disc.
                    double d = Math.Sqrt((x - CenterX) * (x - CenterX) + (y - CenterY) * (y - CenterY));
                    if (d > Radius + 6)
                        Assert.Equal(Background(x, y), (px[i], px[i + 1], px[i + 2]));
                    continue;
                }
                var (r, g, b) = Background(x, y);
                error += Math.Abs(px[i] - r) + Math.Abs(px[i + 1] - g) + Math.Abs(px[i + 2] - b);
                count++;
                if (px[i] > 150 && px[i + 1] < 80) reddish++;
            }

        double meanError = error / (3.0 * count);
        Assert.Equal(0, reddish);
        Assert.True(meanError < 20, $"mean error {meanError:0.0} per channel inside the disc");
    }

    [RequiresLamaModelFact]
    public void LargeImage_FillsThroughTheDownscaledModel()
    {
        // 4× the scene: the context is scaled down to the 512 model input and the answer back up.
        const int W = 2560, H = 1920;
        using var small = Scene(640, 480);
        using var scene = PixelOps.ResampleNearest(small, W, H);
        var selection = DiscSelection(640, 480);
        selection.ScaleBy(4, 4);

        using var filled = Fill(scene, selection);

        var px = filled.PixelsReadOnly;
        int reddish = 0;
        for (int y = 0; y < H; y += 2)
            for (int x = 0; x < W; x += 2)
            {
                int i = (y * W + x) * 4;
                if (px[i] > 150 && px[i + 1] < 80) reddish++;
            }
        Assert.Equal(0, reddish);
    }
}
