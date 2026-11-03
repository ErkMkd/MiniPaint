using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Document;

/// <summary>
/// Canvas size (crop/extend, no pixel scale) and Image size (scale layer pixels).
/// Image resize uses Skia Mitchell cubic resampling (bicubic-class filter).
/// </summary>
public static class DocumentResize
{
    public const string ImageInterpolationName = "Skia Mitchell cubic (bicubic)";

    public static void ResizeCanvas(PaintDocument document, int newWidth, int newHeight, CanvasAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newHeight);

        int oldW = document.Width;
        int oldH = document.Height;
        if (oldW == newWidth && oldH == newHeight)
            return;

        var (dx, dy) = CanvasAnchorMath.OffsetDelta(anchor, oldW, oldH, newWidth, newHeight);
        if (dx != 0 || dy != 0)
        {
            foreach (var layer in document.Layers)
            {
                layer.OffsetX += dx;
                layer.OffsetY += dy;
            }
        }

        document.SetSize(newWidth, newHeight);
    }

    public static void ResizeImage(PaintDocument document, int newWidth, int newHeight)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newHeight);

        int oldW = document.Width;
        int oldH = document.Height;
        if (oldW == newWidth && oldH == newHeight)
            return;

        double sx = newWidth / (double)oldW;
        double sy = newHeight / (double)oldH;

        foreach (var layer in document.Layers)
        {
            int nw = Math.Max(1, (int)Math.Round(layer.Pixels.Width * sx));
            int nh = Math.Max(1, (int)Math.Round(layer.Pixels.Height * sy));
            var scaled = PixelOps.ScaleBuffer(layer.Pixels, nw, nh);
            layer.ReplacePixels(scaled);
            layer.OffsetX = (int)Math.Round(layer.OffsetX * sx);
            layer.OffsetY = (int)Math.Round(layer.OffsetY * sy);
        }

        document.SetSize(newWidth, newHeight);
    }
}
