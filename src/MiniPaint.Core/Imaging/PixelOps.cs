using MiniPaint.Core.Composition;
using SkiaSharp;

namespace MiniPaint.Core.Imaging;

/// <summary>
/// Pixel-level helpers: alpha blend, crop, clear with alpha, coordinate transforms.
/// </summary>
public static class PixelOps
{
    /// <summary>
    /// Premultiplied-aware "over" blend of src onto dst at one pixel (both unpremultiplied RGBA).
    /// </summary>
    public static void BlendOver(
        byte srcR, byte srcG, byte srcB, byte srcA,
        ref byte dstR, ref byte dstG, ref byte dstB, ref byte dstA)
    {
        BlendOps.BlendPixel(BlendMode.Normal, srcR, srcG, srcB, srcA, ref dstR, ref dstG, ref dstB, ref dstA, 1f);
    }

    /// <summary>
    /// Blend src onto dst with opacity and blend mode for the source layer.
    /// </summary>
    public static void BlendLayer(
        PixelBuffer dst,
        PixelBuffer src,
        int offsetX,
        int offsetY,
        float opacity,
        BlendMode blendMode = BlendMode.Normal)
    {
        ArgumentNullException.ThrowIfNull(dst);
        ArgumentNullException.ThrowIfNull(src);
        opacity = Math.Clamp(opacity, 0f, 1f);
        if (opacity <= 0f) return;

        var dstPx = dst.Pixels;
        var srcPx = src.PixelsReadOnly;

        int x0 = Math.Max(0, offsetX);
        int y0 = Math.Max(0, offsetY);
        int x1 = Math.Min(dst.Width, offsetX + src.Width);
        int y1 = Math.Min(dst.Height, offsetY + src.Height);

        for (int y = y0; y < y1; y++)
        {
            int sy = y - offsetY;
            for (int x = x0; x < x1; x++)
            {
                int sx = x - offsetX;
                int si = (sy * src.Width + sx) * 4;
                int di = (y * dst.Width + x) * 4;

                byte sa = srcPx[si + 3];
                if (sa == 0) continue;

                byte sr = srcPx[si];
                byte sg = srcPx[si + 1];
                byte sb = srcPx[si + 2];

                byte dr = dstPx[di];
                byte dg = dstPx[di + 1];
                byte db = dstPx[di + 2];
                byte da = dstPx[di + 3];

                BlendOps.BlendPixel(blendMode, sr, sg, sb, sa, ref dr, ref dg, ref db, ref da, opacity);

                dstPx[di] = dr;
                dstPx[di + 1] = dg;
                dstPx[di + 2] = db;
                dstPx[di + 3] = da;
            }
        }
    }

    /// <summary>
    /// Extract a rectangular region from a layer into a new buffer (document coords → layer-local).
    /// Transparent outside the layer.
    /// </summary>
    public static PixelBuffer ExtractRegion(
        PixelBuffer layerPixels,
        int layerOffsetX,
        int layerOffsetY,
        int docX,
        int docY,
        int width,
        int height)
    {
        var result = new PixelBuffer(width, height);
        var dst = result.Pixels;
        var src = layerPixels.PixelsReadOnly;

        for (int y = 0; y < height; y++)
        {
            int docYY = docY + y;
            int ly = docYY - layerOffsetY;
            if ((uint)ly >= (uint)layerPixels.Height) continue;

            for (int x = 0; x < width; x++)
            {
                int docXX = docX + x;
                int lx = docXX - layerOffsetX;
                if ((uint)lx >= (uint)layerPixels.Width) continue;

                int si = (ly * layerPixels.Width + lx) * 4;
                int di = (y * width + x) * 4;
                dst[di] = src[si];
                dst[di + 1] = src[si + 1];
                dst[di + 2] = src[si + 2];
                dst[di + 3] = src[si + 3];
            }
        }

        return result;
    }

    /// <summary>
    /// Clear a rectangular region on a layer (document coords) to transparent.
    /// </summary>
    public static void ClearRegion(
        PixelBuffer layerPixels,
        int layerOffsetX,
        int layerOffsetY,
        int docX,
        int docY,
        int width,
        int height)
    {
        var px = layerPixels.Pixels;
        for (int y = 0; y < height; y++)
        {
            int ly = docY + y - layerOffsetY;
            if ((uint)ly >= (uint)layerPixels.Height) continue;

            for (int x = 0; x < width; x++)
            {
                int lx = docX + x - layerOffsetX;
                if ((uint)lx >= (uint)layerPixels.Width) continue;

                int i = (ly * layerPixels.Width + lx) * 4;
                px[i] = 0;
                px[i + 1] = 0;
                px[i + 2] = 0;
                px[i + 3] = 0;
            }
        }
    }

    /// <summary>
    /// Paste source pixels onto destination layer at document position (alpha over).
    /// </summary>
    public static void PasteOver(
        PixelBuffer destLayer,
        int destOffsetX,
        int destOffsetY,
        PixelBuffer source,
        int docX,
        int docY)
    {
        int offsetX = docX - destOffsetX;
        int offsetY = docY - destOffsetY;
        BlendLayer(destLayer, source, offsetX, offsetY, 1f);
    }

    /// <summary>
    /// Axis-aligned bounds of pixels with alpha &gt; 0. Returns false if the buffer is fully transparent.
    /// </summary>
    public static bool TryGetOpaqueBounds(PixelBuffer buffer, out int x, out int y, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        x = y = width = height = 0;

        var px = buffer.PixelsReadOnly;
        int minX = buffer.Width;
        int minY = buffer.Height;
        int maxX = -1;
        int maxY = -1;

        for (int row = 0; row < buffer.Height; row++)
        {
            int rowOff = row * buffer.Width * 4;
            for (int col = 0; col < buffer.Width; col++)
            {
                if (px[rowOff + col * 4 + 3] == 0) continue;
                if (col < minX) minX = col;
                if (col > maxX) maxX = col;
                if (row < minY) minY = row;
                if (row > maxY) maxY = row;
            }
        }

        if (maxX < 0) return false;

        x = minX;
        y = minY;
        width = maxX - minX + 1;
        height = maxY - minY + 1;
        return true;
    }

    /// <summary>
    /// Copy a rectangular region of a buffer into a new buffer (layer-local coords).
    /// </summary>
    public static PixelBuffer Crop(PixelBuffer source, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (x + width > source.Width || y + height > source.Height)
            throw new ArgumentOutOfRangeException(nameof(width), "Crop rectangle exceeds source bounds.");

        var result = new PixelBuffer(width, height);
        var dst = result.Pixels;
        var src = source.PixelsReadOnly;
        for (int row = 0; row < height; row++)
        {
            int si = ((y + row) * source.Width + x) * 4;
            int di = row * width * 4;
            src.Slice(si, width * 4).CopyTo(dst.Slice(di, width * 4));
        }

        return result;
    }

    /// <summary>
    /// Crop to opaque content. Returns null if fully transparent.
    /// <paramref name="originX"/>/<paramref name="originY"/> are the crop origin in the source buffer.
    /// </summary>
    public static PixelBuffer? CropToOpaqueContent(PixelBuffer source, out int originX, out int originY)
    {
        originX = originY = 0;
        if (!TryGetOpaqueBounds(source, out int x, out int y, out int w, out int h))
            return null;
        originX = x;
        originY = y;
        return Crop(source, x, y, w, h);
    }

    public static (int x, int y) ScreenToDocument(
        double screenX, double screenY,
        double panX, double panY,
        double zoom)
    {
        if (zoom <= 0) throw new ArgumentOutOfRangeException(nameof(zoom));
        int x = (int)Math.Floor((screenX - panX) / zoom);
        int y = (int)Math.Floor((screenY - panY) / zoom);
        return (x, y);
    }

    public static (double x, double y) DocumentToScreen(
        int docX, int docY,
        double panX, double panY,
        double zoom)
    {
        return (docX * zoom + panX, docY * zoom + panY);
    }

    public static (double panX, double panY) ZoomAtCursor(
        double oldZoom, double newZoom,
        double cursorScreenX, double cursorScreenY,
        double panX, double panY)
    {
        if (oldZoom <= 0 || newZoom <= 0)
            throw new ArgumentOutOfRangeException(nameof(newZoom));

        // Keep the document point under the cursor fixed.
        double docX = (cursorScreenX - panX) / oldZoom;
        double docY = (cursorScreenY - panY) / oldZoom;
        double newPanX = cursorScreenX - docX * newZoom;
        double newPanY = cursorScreenY - docY * newZoom;
        return (newPanX, newPanY);
    }

    /// <summary>
    /// Scale a buffer with Skia Mitchell cubic resampling (bicubic-class). Caller owns the result.
    /// </summary>
    public static PixelBuffer ScaleBuffer(PixelBuffer source, int newWidth, int newHeight)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newHeight);

        if (source.Width == newWidth && source.Height == newHeight)
            return source.Clone();

        using var srcBmp = ImageCodec.ToSkBitmap(source);
        var info = new SKImageInfo(newWidth, newHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var dstBmp = new SKBitmap(info);
        dstBmp.Erase(SKColors.Transparent);

        using (var canvas = new SKCanvas(dstBmp))
        {
            var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
            using var img = SKImage.FromBitmap(srcBmp);
            var dest = SKRect.Create(0, 0, newWidth, newHeight);
            canvas.DrawImage(img, dest, sampling);
        }

        var pixels = new byte[newWidth * newHeight * 4];
        System.Runtime.InteropServices.Marshal.Copy(dstBmp.GetPixels(), pixels, 0, pixels.Length);
        return new PixelBuffer(newWidth, newHeight, pixels);
    }
}
