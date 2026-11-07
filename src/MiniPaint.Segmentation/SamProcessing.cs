using Microsoft.ML.OnnxRuntime.Tensors;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Segmentation;

/// <summary>Click on an object, or box around it, in document pixel coordinates.</summary>
public sealed record SamPrompt(double X0, double Y0, double X1, double Y1, bool IsBox)
{
    public static SamPrompt Point(double x, double y) => new(x, y, x, y, false);

    public static SamPrompt Box(double x0, double y0, double x1, double y1) =>
        new(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1), true);
}

/// <summary>Image ready for the encoder: RGB floats (0–255), longest side resized to 1024.</summary>
public sealed record SamEncoderInput(DenseTensor<float> Pixels, int Width, int Height, int ResizedWidth, int ResizedHeight);

/// <summary>
/// Pre- and post-processing around the MobileSAM ONNX models (pure functions, no model needed):
/// image resize, prompt coordinates, mask choice and upscaling to document size.
/// </summary>
public static class SamProcessing
{
    /// <summary>Encoder input size: the longest image side is resized to this.</summary>
    public const int InputSize = 1024;

    /// <summary>Gray that transparent pixels are blended onto (the model has no alpha).</summary>
    public const int Background = 128;

    public static (int Width, int Height) ResizedSize(int width, int height)
    {
        double scale = InputSize / (double)Math.Max(width, height);
        return (Math.Max(1, (int)(width * scale + 0.5)), Math.Max(1, (int)(height * scale + 0.5)));
    }

    /// <summary>Resize (high-quality) and convert to the encoder's H × W × RGB float layout.</summary>
    public static SamEncoderInput ToEncoderInput(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var (rw, rh) = ResizedSize(image.Width, image.Height);
        using var resized = PixelOps.ScaleBuffer(image, rw, rh);

        var data = new float[rh * rw * 3];
        var px = resized.PixelsReadOnly;
        for (int i = 0, o = 0; i < px.Length; i += 4, o += 3)
        {
            int a = px[i + 3];
            data[o] = (px[i] * a + Background * (255 - a)) / 255f;
            data[o + 1] = (px[i + 1] * a + Background * (255 - a)) / 255f;
            data[o + 2] = (px[i + 2] * a + Background * (255 - a)) / 255f;
        }
        return new SamEncoderInput(new DenseTensor<float>(data, [rh, rw, 3]), image.Width, image.Height, rw, rh);
    }

    /// <summary>
    /// Decoder point inputs in the resized frame. A click is one foreground point plus the padding
    /// point (label −1) the SAM export expects without a box; a box is its two corners (labels 2, 3).
    /// </summary>
    public static (float[] Coords, float[] Labels) EncodePrompt(
        SamPrompt prompt, int width, int height, int resizedWidth, int resizedHeight)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        float sx = resizedWidth / (float)width;
        float sy = resizedHeight / (float)height;
        return prompt.IsBox
            ? ([(float)prompt.X0 * sx, (float)prompt.Y0 * sy, (float)prompt.X1 * sx, (float)prompt.Y1 * sy], [2f, 3f])
            : ([(float)prompt.X0 * sx, (float)prompt.Y0 * sy, 0f, 0f], [1f, -1f]);
    }

    /// <summary>
    /// The multi-mask decoder returns 4 masks: 0 is the single-object answer (best for a box),
    /// 1–3 are alternatives for an ambiguous click, of which SAM keeps the highest predicted IoU.
    /// </summary>
    public static int ChooseMask(ReadOnlySpan<float> iouPredictions, bool isBox)
    {
        if (isBox || iouPredictions.Length < 2) return 0;
        int best = 1;
        for (int k = 2; k < iouPredictions.Length; k++)
        {
            if (iouPredictions[k] > iouPredictions[best])
                best = k;
        }
        return best;
    }

    /// <summary>
    /// Bilinear upscale of mask logits (resized frame) to the document, as coverage 0–255:
    /// hard edges keep logit &gt; 0, soft edges use the logistic curve for anti-aliased borders.
    /// </summary>
    public static byte[] LogitsToCoverage(
        float[] logits, int sourceWidth, int sourceHeight, int width, int height, bool softEdges)
    {
        ArgumentNullException.ThrowIfNull(logits);
        var coverage = new byte[width * height];
        float scaleX = sourceWidth / (float)width;
        float scaleY = sourceHeight / (float)height;

        Parallel.For(0, height, y =>
        {
            float fy = Math.Clamp((y + 0.5f) * scaleY - 0.5f, 0, sourceHeight - 1);
            int y0 = (int)fy;
            int y1 = Math.Min(y0 + 1, sourceHeight - 1);
            float ty = fy - y0;
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                float fx = Math.Clamp((x + 0.5f) * scaleX - 0.5f, 0, sourceWidth - 1);
                int x0 = (int)fx;
                int x1 = Math.Min(x0 + 1, sourceWidth - 1);
                float tx = fx - x0;
                float top = logits[y0 * sourceWidth + x0] * (1 - tx) + logits[y0 * sourceWidth + x1] * tx;
                float bottom = logits[y1 * sourceWidth + x0] * (1 - tx) + logits[y1 * sourceWidth + x1] * tx;
                float logit = top * (1 - ty) + bottom * ty;

                coverage[row + x] = softEdges
                    ? (byte)(255f / (1f + MathF.Exp(-logit)) + 0.5f)
                    : logit > 0 ? (byte)255 : (byte)0;
            }
        });
        return coverage;
    }
}
