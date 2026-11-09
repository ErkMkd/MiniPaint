using Microsoft.ML.OnnxRuntime.Tensors;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Selection;

namespace MiniPaint.Inpainting;

/// <summary>Rectangle in document pixels.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>
/// Geometry of one fill, in document pixels. <see cref="Crop"/> is the context the model sees,
/// scaled to <see cref="ModelWidth"/> × <see cref="ModelHeight"/> (then mirror-padded to the square
/// model input). <see cref="Region"/> bounds the selection grown by <see cref="Expand"/> pixels,
/// plus one: the fill changes no pixel outside it.
/// </summary>
public sealed record InpaintPlan(PixelRect Crop, PixelRect Region, int Expand, int ModelWidth, int ModelHeight);

/// <summary>Model tensors; HasContext is false when every pixel the model sees is a hole.</summary>
public sealed record LamaInput(DenseTensor<float> Image, DenseTensor<float> Mask, bool HasContext);

/// <summary>Model call: image and mask tensors in, RGB image (1 × 3 × 512 × 512, 0–255) out.</summary>
public delegate float[] InpaintModel(DenseTensor<float> image, DenseTensor<float> mask);

/// <summary>Layer pixels around the selection and a copy of the selection, safe to use off the UI thread.</summary>
public sealed class InpaintRequest : IDisposable
{
    internal InpaintRequest(InpaintPlan plan, PixelBuffer context, SelectionState selection)
    {
        Plan = plan;
        Context = context;
        Selection = selection;
    }

    public InpaintPlan Plan { get; }

    /// <summary>Layer pixels of <see cref="InpaintPlan.Crop"/> (transparent outside the layer).</summary>
    public PixelBuffer Context { get; }

    public SelectionState Selection { get; }

    public void Dispose() => Context.Dispose();
}

/// <summary>
/// The fill as layer content for <see cref="Region"/>: rebuilt colors, opaque over the selection
/// and fading out across the grown edge, transparent beyond. Laid over the source it hides the object.
/// </summary>
public sealed class InpaintResult : IDisposable
{
    public InpaintResult(PixelRect region, PixelBuffer pixels)
    {
        Region = region;
        Pixels = pixels;
    }

    public PixelRect Region { get; }
    public PixelBuffer Pixels { get; }

    public void Dispose() => Pixels.Dispose();
}

/// <summary>
/// Pre- and post-processing around the LaMa ONNX model (pure functions, the model is passed in):
/// context crop, hole growth, resize to the 512 × 512 model input and back, soft blend weights.
/// </summary>
public static class InpaintProcessing
{
    /// <summary>The model's fixed input size.</summary>
    public const int ModelSize = 512;

    /// <summary>Gray that transparent pixels are blended onto (the model has no alpha).</summary>
    public const int Background = 128;

    /// <summary>The context crop is this many times the selection's longest side (at least <see cref="ModelSize"/>).</summary>
    public const double ContextScale = 2.0;

    /// <summary>The hole grows by this many model pixels, to swallow the halo along the object's edge…</summary>
    public const double ExpandModelPixels = 2;

    /// <summary>…and by at least this many document pixels.</summary>
    public const int MinExpand = 3;

    /// <summary>Fill geometry for the selection, or null when it does not touch the document.</summary>
    public static InpaintPlan? Plan(SelectionState selection, int docWidth, int docHeight)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!selection.HasSelection || docWidth <= 0 || docHeight <= 0) return null;
        int sx0 = Math.Max(0, selection.X);
        int sy0 = Math.Max(0, selection.Y);
        int sx1 = Math.Min(docWidth, selection.Right);
        int sy1 = Math.Min(docHeight, selection.Bottom);
        if (sx1 <= sx0 || sy1 <= sy0) return null;

        // Square context centered on the selection, clipped to the document.
        int side = Math.Max(sx1 - sx0, sy1 - sy0);
        int size = Math.Max(ModelSize, (int)Math.Ceiling(side * ContextScale));
        int cw = Math.Min(size, docWidth);
        int ch = Math.Min(size, docHeight);
        int cx = Math.Clamp((sx0 + sx1 - cw) / 2, 0, docWidth - cw);
        int cy = Math.Clamp((sy0 + sy1 - ch) / 2, 0, docHeight - ch);

        double scale = ModelSize / (double)Math.Max(cw, ch);
        int mw = Math.Clamp((int)Math.Round(cw * scale), 1, ModelSize);
        int mh = Math.Clamp((int)Math.Round(ch * scale), 1, ModelSize);
        int expand = Math.Max(MinExpand, (int)Math.Ceiling(ExpandModelPixels / scale));

        // One pixel beyond the grown hole, so its edge always has untouched pixels to fade to.
        int margin = expand + 1;
        int rx0 = Math.Max(cx, sx0 - margin);
        int ry0 = Math.Max(cy, sy0 - margin);
        int rx1 = Math.Min(cx + cw, sx1 + margin);
        int ry1 = Math.Min(cy + ch, sy1 + margin);
        return new InpaintPlan(
            new PixelRect(cx, cy, cw, ch), new PixelRect(rx0, ry0, rx1 - rx0, ry1 - ry0), expand, mw, mh);
    }

    /// <summary>
    /// Copy what a fill of <paramref name="selection"/> needs from a layer (call on the UI thread;
    /// the request can then be processed anywhere). Null when the selection misses the document.
    /// </summary>
    public static InpaintRequest? CreateRequest(
        PixelBuffer layerPixels, int layerOffsetX, int layerOffsetY,
        SelectionState selection, int docWidth, int docHeight)
    {
        ArgumentNullException.ThrowIfNull(layerPixels);
        var plan = Plan(selection, docWidth, docHeight);
        if (plan is null) return null;
        var c = plan.Crop;
        var context = PixelOps.ExtractRegion(layerPixels, layerOffsetX, layerOffsetY, c.X, c.Y, c.Width, c.Height);
        return new InpaintRequest(plan, context, selection.Clone());
    }

    /// <summary>
    /// The whole fill around <paramref name="model"/>: grow the hole, build the model input, scale
    /// the answer back to the document and fade its edge (alpha). Null when nothing around the
    /// selection is left to rebuild from (the hole covers everything the model would see).
    /// </summary>
    public static InpaintResult? Inpaint(InpaintRequest request, InpaintModel model)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(model);
        var plan = request.Plan;
        var region = plan.Region;
        var hole = Grow(HoleMask(request.Selection, region), region.Width, region.Height, plan.Expand);

        var input = ToModelInput(request.Context, hole, plan);
        if (!input.HasContext) return null;
        var output = model(input.Image, input.Mask);

        using var rebuilt = FromModelOutput(output, plan.ModelWidth, plan.ModelHeight);
        using var full = PixelOps.ScaleBuffer(rebuilt, plan.Crop.Width, plan.Crop.Height);
        var fill = PixelOps.Crop(full, region.X - plan.Crop.X, region.Y - plan.Crop.Y, region.Width, region.Height);
        var weights = EdgeWeights(hole, region.Width, region.Height, plan.Expand);
        var px = fill.Pixels;
        for (int i = 0; i < weights.Length; i++)
            px[i * 4 + 3] = weights[i];
        return new InpaintResult(region, fill);
    }

    /// <summary>1 where the selection covers a pixel of <paramref name="region"/> at all, else 0.</summary>
    public static byte[] HoleMask(SelectionState selection, PixelRect region)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var hole = new byte[region.Width * region.Height];
        if (!selection.HasSelection) return hole;
        int x0 = Math.Max(region.X, selection.X);
        int x1 = Math.Min(region.Right, selection.Right);
        int y0 = Math.Max(region.Y, selection.Y);
        int y1 = Math.Min(region.Bottom, selection.Bottom);
        if (x1 <= x0 || y1 <= y0) return hole;

        var coverage = selection.Mask;
        for (int y = y0; y < y1; y++)
        {
            var row = hole.AsSpan((y - region.Y) * region.Width + (x0 - region.X), x1 - x0);
            if (selection.IsRectangular)
            {
                row.Fill(1);
                continue;
            }
            var src = coverage.Slice((y - selection.Y) * selection.Width + (x0 - selection.X), x1 - x0);
            for (int i = 0; i < row.Length; i++)
                row[i] = src[i] != 0 ? (byte)1 : (byte)0;
        }
        return hole;
    }

    /// <summary>The hole (1) grown by <paramref name="radius"/> pixels.</summary>
    public static byte[] Grow(byte[] hole, int width, int height, int radius)
    {
        var distance = Distance(hole, width, height, 1);
        var grown = new byte[hole.Length];
        int limit = 3 * radius;
        for (int i = 0; i < grown.Length; i++)
            grown[i] = distance[i] <= limit ? (byte)1 : (byte)0;
        return grown;
    }

    /// <summary>
    /// Fill opacity per pixel: 0 outside the hole, rising across the <paramref name="ramp"/> pixels
    /// along its edge to 255 further in, so the fill melts into the untouched image.
    /// </summary>
    public static byte[] EdgeWeights(byte[] hole, int width, int height, int ramp)
    {
        var distance = Distance(hole, width, height, 0);
        int full = 3 * Math.Max(1, ramp);
        var weights = new byte[hole.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            if (hole[i] == 0) continue;
            weights[i] = distance[i] >= full ? (byte)255 : (byte)(distance[i] * 255 / full);
        }
        return weights;
    }

    /// <summary>
    /// Chamfer 3-4 distance from each pixel to the nearest pixel equal to <paramref name="target"/>,
    /// in thirds of a pixel (3 = one step), saturating at <see cref="ushort.MaxValue"/>.
    /// Pixels beyond the edges do not count as targets.
    /// </summary>
    public static ushort[] Distance(byte[] mask, int width, int height, byte target)
    {
        ArgumentNullException.ThrowIfNull(mask);
        const int Far = ushort.MaxValue;
        var d = new ushort[width * height];
        for (int i = 0; i < d.Length; i++)
            d[i] = mask[i] == target ? (ushort)0 : (ushort)Far;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0, i = y * width; x < width; x++, i++)
            {
                int v = d[i];
                if (v == 0) continue;
                if (x > 0) v = Math.Min(v, d[i - 1] + 3);
                if (y > 0)
                {
                    v = Math.Min(v, d[i - width] + 3);
                    if (x > 0) v = Math.Min(v, d[i - width - 1] + 4);
                    if (x < width - 1) v = Math.Min(v, d[i - width + 1] + 4);
                }
                d[i] = (ushort)Math.Min(v, Far);
            }
        }
        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = width - 1, i = y * width + x; x >= 0; x--, i--)
            {
                int v = d[i];
                if (v == 0) continue;
                if (x < width - 1) v = Math.Min(v, d[i + 1] + 3);
                if (y < height - 1)
                {
                    v = Math.Min(v, d[i + width] + 3);
                    if (x < width - 1) v = Math.Min(v, d[i + width + 1] + 4);
                    if (x > 0) v = Math.Min(v, d[i + width - 1] + 4);
                }
                d[i] = (ushort)Math.Min(v, Far);
            }
        }
        return d;
    }

    /// <summary>
    /// Model tensors for <paramref name="plan"/>: the context resized to ModelWidth × ModelHeight and
    /// the hole (<see cref="InpaintPlan.Region"/>-sized), both mirror-padded to 512 × 512. Hole pixels
    /// are blanked in the image, as LaMa was trained.
    /// </summary>
    public static LamaInput ToModelInput(PixelBuffer context, byte[] hole, InpaintPlan plan)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plan);
        int mw = plan.ModelWidth;
        int mh = plan.ModelHeight;
        using var flat = Flatten(context);
        using var small = mw * mh < context.Width * context.Height
            ? AreaDownscale(flat, mw, mh)
            : PixelOps.ScaleBuffer(flat, mw, mh);
        var modelHole = ModelMask(hole, plan);
        bool hasContext = modelHole.AsSpan().IndexOf((byte)0) >= 0;

        const int Plane = ModelSize * ModelSize;
        var image = new DenseTensor<float>([1, 3, ModelSize, ModelSize]);
        var mask = new DenseTensor<float>([1, 1, ModelSize, ModelSize]);
        var img = image.Buffer.Span;
        var msk = mask.Buffer.Span;
        var px = small.PixelsReadOnly;
        for (int y = 0; y < ModelSize; y++)
        {
            int row = Mirror(y, mh) * mw;
            for (int x = 0; x < ModelSize; x++)
            {
                int s = row + Mirror(x, mw);
                int o = y * ModelSize + x;
                if (modelHole[s] != 0)
                {
                    msk[o] = 1;
                    continue;
                }
                img[o] = px[s * 4] / 255f;
                img[Plane + o] = px[s * 4 + 1] / 255f;
                img[2 * Plane + o] = px[s * 4 + 2] / 255f;
            }
        }
        return new LamaInput(image, mask, hasContext);
    }

    /// <summary>
    /// The hole at model resolution (ModelWidth × ModelHeight): a model pixel is a hole as soon as
    /// any document pixel under it is, so nothing of the object survives the downscale.
    /// </summary>
    public static byte[] ModelMask(byte[] hole, InpaintPlan plan)
    {
        ArgumentNullException.ThrowIfNull(hole);
        ArgumentNullException.ThrowIfNull(plan);
        var crop = plan.Crop;
        var region = plan.Region;
        int mw = plan.ModelWidth;
        var (firstCol, lastCol) = Coverers(crop.Width, mw);
        var (firstRow, lastRow) = Coverers(crop.Height, plan.ModelHeight);

        var mask = new byte[mw * plan.ModelHeight];
        for (int y = 0; y < region.Height; y++)
        {
            int cy = region.Y - crop.Y + y;
            for (int x = 0; x < region.Width; x++)
            {
                if (hole[y * region.Width + x] == 0) continue;
                int cx = region.X - crop.X + x;
                for (int my = firstRow[cy]; my <= lastRow[cy]; my++)
                    mask.AsSpan(my * mw + firstCol[cx], lastCol[cx] - firstCol[cx] + 1).Fill(1);
            }
        }
        return mask;
    }

    /// <summary>
    /// For each of <paramref name="count"/> resized pixels, the source pixels [Start, End) under it
    /// when <paramref name="sourceLength"/> pixels are resized to <paramref name="count"/>.
    /// </summary>
    public static (int[] Start, int[] End) SourceSpans(int sourceLength, int count)
    {
        var start = new int[count];
        var end = new int[count];
        for (int i = 0; i < count; i++)
        {
            start[i] = (int)((long)i * sourceLength / count);
            int ceil = (int)(((long)(i + 1) * sourceLength + count - 1) / count);
            end[i] = Math.Min(sourceLength, Math.Max(start[i] + 1, ceil));
        }
        return (start, end);
    }

    /// <summary>For each source pixel, the first and last resized pixels whose span covers it.</summary>
    private static (int[] First, int[] Last) Coverers(int sourceLength, int count)
    {
        var (start, end) = SourceSpans(sourceLength, count);
        var first = new int[sourceLength];
        var last = new int[sourceLength];
        Array.Fill(first, int.MaxValue);
        Array.Fill(last, -1);
        for (int i = 0; i < count; i++)
        {
            for (int s = start[i]; s < end[i]; s++)
            {
                first[s] = Math.Min(first[s], i);
                last[s] = Math.Max(last[s], i);
            }
        }
        return (first, last);
    }

    /// <summary>Downscale by averaging the source pixels under each output pixel (no aliasing at large factors).</summary>
    public static PixelBuffer AreaDownscale(PixelBuffer source, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        var (xs, xe) = SourceSpans(source.Width, width);
        var (ys, ye) = SourceSpans(source.Height, height);
        var result = new byte[width * height * 4];
        Parallel.For(0, height, y =>
        {
            var src = source.PixelsReadOnly;
            var sums = new int[width * 4];
            for (int sy = ys[y]; sy < ye[y]; sy++)
            {
                int row = sy * source.Width * 4;
                for (int x = 0; x < width; x++)
                {
                    int o = x * 4;
                    for (int sx = xs[x]; sx < xe[x]; sx++)
                    {
                        int i = row + sx * 4;
                        sums[o] += src[i];
                        sums[o + 1] += src[i + 1];
                        sums[o + 2] += src[i + 2];
                        sums[o + 3] += src[i + 3];
                    }
                }
            }
            int rows = ye[y] - ys[y];
            for (int x = 0; x < width; x++)
            {
                int n = rows * (xe[x] - xs[x]);
                int o = x * 4;
                int d = (y * width + x) * 4;
                for (int c = 0; c < 4; c++)
                    result[d + c] = (byte)((sums[o + c] + n / 2) / n);
            }
        });
        return new PixelBuffer(width, height, result);
    }

    /// <summary>The top-left <paramref name="width"/> × <paramref name="height"/> of the model output (CHW, 0–255), opaque.</summary>
    public static PixelBuffer FromModelOutput(ReadOnlySpan<float> output, int width, int height)
    {
        const int Plane = ModelSize * ModelSize;
        if (output.Length < 3 * Plane)
            throw new ArgumentException($"Unexpected model output size {output.Length}.", nameof(output));

        var buffer = new PixelBuffer(width, height);
        var px = buffer.Pixels;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int o = y * ModelSize + x;
                int d = (y * width + x) * 4;
                px[d] = ToByte(output[o]);
                px[d + 1] = ToByte(output[Plane + o]);
                px[d + 2] = ToByte(output[2 * Plane + o]);
                px[d + 3] = 255;
            }
        }
        return buffer;
    }

    /// <summary>Index into [0, length) reflected at the edges, edge pixels repeated: … 1 0 | 0 1 … n−1 | n−1 n−2 …</summary>
    public static int Mirror(int index, int length)
    {
        int period = 2 * length;
        int m = index % period;
        if (m < 0) m += period;
        return m < length ? m : period - 1 - m;
    }

    /// <summary>Opaque copy, transparent pixels blended onto <see cref="Background"/> gray.</summary>
    private static PixelBuffer Flatten(PixelBuffer source)
    {
        var copy = source.Clone();
        var px = copy.Pixels;
        for (int i = 0; i < px.Length; i += 4)
        {
            int a = px[i + 3];
            if (a == 255) continue;
            for (int c = 0; c < 3; c++)
                px[i + c] = (byte)((px[i + c] * a + Background * (255 - a) + 127) / 255);
            px[i + 3] = 255;
        }
        return copy;
    }

    // NaN fails both comparisons and becomes 0.
    private static byte ToByte(float v) => v >= 255f ? (byte)255 : v > 0f ? (byte)(v + 0.5f) : (byte)0;
}
