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
        if (srcA == 0) return;
        if (srcA == 255 || dstA == 0)
        {
            dstR = srcR;
            dstG = srcG;
            dstB = srcB;
            dstA = srcA;
            return;
        }

        // outA = srcA + dstA * (1 - srcA)
        int invSrcA = 255 - srcA;
        int outA = srcA + (dstA * invSrcA + 127) / 255;
        if (outA == 0)
        {
            dstR = dstG = dstB = dstA = 0;
            return;
        }

        // Unpremultiply result after blending premultiplied channels.
        int outR = (srcR * srcA + dstR * dstA * invSrcA / 255 + 127) / 255;
        int outG = (srcG * srcA + dstG * dstA * invSrcA / 255 + 127) / 255;
        int outB = (srcB * srcA + dstB * dstA * invSrcA / 255 + 127) / 255;

        dstR = (byte)Math.Clamp((outR * 255 + outA / 2) / outA, 0, 255);
        dstG = (byte)Math.Clamp((outG * 255 + outA / 2) / outA, 0, 255);
        dstB = (byte)Math.Clamp((outB * 255 + outA / 2) / outA, 0, 255);
        dstA = (byte)Math.Clamp(outA, 0, 255);
    }

    /// <summary>
    /// Blend src onto dst with uniform opacity multiplier (0..1) for the source layer.
    /// </summary>
    public static void BlendLayer(
        PixelBuffer dst,
        PixelBuffer src,
        int offsetX,
        int offsetY,
        float opacity)
    {
        ArgumentNullException.ThrowIfNull(dst);
        ArgumentNullException.ThrowIfNull(src);
        opacity = Math.Clamp(opacity, 0f, 1f);
        if (opacity <= 0f) return;

        int opacityByte = (int)Math.Round(opacity * 255f);
        if (opacityByte <= 0) return;

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

                int a = (sa * opacityByte + 127) / 255;
                if (a == 0) continue;

                byte sr = srcPx[si];
                byte sg = srcPx[si + 1];
                byte sb = srcPx[si + 2];

                byte dr = dstPx[di];
                byte dg = dstPx[di + 1];
                byte db = dstPx[di + 2];
                byte da = dstPx[di + 3];

                BlendOver(sr, sg, sb, (byte)a, ref dr, ref dg, ref db, ref da);

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
}
