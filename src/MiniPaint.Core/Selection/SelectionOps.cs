using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Selection;

public static class SelectionOps
{
    /// <summary>
    /// Copy pixels from the active layer within the selection bounds (transparent outside the layer).
    /// With a mask, alpha is scaled by coverage, so unselected pixels come out transparent.
    /// </summary>
    public static PixelBuffer? CopyFromActiveLayer(PaintDocument document, SelectionState selection)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selection);
        if (!selection.HasSelection) return null;

        var layer = document.ActiveLayer;
        if (layer is null) return null;

        var copied = PixelOps.ExtractRegion(
            layer.Pixels,
            layer.OffsetX,
            layer.OffsetY,
            selection.X,
            selection.Y,
            selection.Width,
            selection.Height);

        if (!selection.IsRectangular)
        {
            var px = copied.Pixels;
            var mask = selection.Mask;
            for (int i = 0; i < mask.Length; i++)
                px[i * 4 + 3] = ScaleAlpha(px, i * 4, mask[i]);
        }

        return copied;
    }

    /// <summary>
    /// Cut: copy then clear the selection on the active layer. Soft edges leave the
    /// complementary partial alpha behind.
    /// </summary>
    public static PixelBuffer? CutFromActiveLayer(PaintDocument document, SelectionState selection)
    {
        var copied = CopyFromActiveLayer(document, selection);
        if (copied is null) return null;

        var layer = document.ActiveLayer!;
        if (selection.IsRectangular)
        {
            PixelOps.ClearRegion(
                layer.Pixels,
                layer.OffsetX,
                layer.OffsetY,
                selection.X,
                selection.Y,
                selection.Width,
                selection.Height);
        }
        else
        {
            ClearMasked(layer, selection);
        }

        document.IsDirty = true;
        return copied;
    }

    /// <summary>White RGBA buffer the size of the selection bounds, alpha = coverage.</summary>
    public static PixelBuffer MaskToPixelBuffer(SelectionState selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!selection.HasSelection)
            throw new InvalidOperationException("No selection.");

        var buffer = new PixelBuffer(selection.Width, selection.Height);
        if (selection.IsRectangular)
        {
            buffer.Clear(255, 255, 255, 255);
            return buffer;
        }

        var px = buffer.Pixels;
        var mask = selection.Mask;
        for (int i = 0; i < mask.Length; i++)
        {
            int o = i * 4;
            px[o] = px[o + 1] = px[o + 2] = 255;
            px[o + 3] = mask[i];
        }
        return buffer;
    }

    private static void ClearMasked(Layer layer, SelectionState selection)
    {
        var px = layer.Pixels.Pixels;
        int lw = layer.Pixels.Width;
        int lh = layer.Pixels.Height;
        var mask = selection.Mask;

        for (int y = 0; y < selection.Height; y++)
        {
            int ly = selection.Y + y - layer.OffsetY;
            if ((uint)ly >= (uint)lh) continue;

            for (int x = 0; x < selection.Width; x++)
            {
                int lx = selection.X + x - layer.OffsetX;
                if ((uint)lx >= (uint)lw) continue;

                byte coverage = mask[y * selection.Width + x];
                if (coverage == 0) continue;
                int i = (ly * lw + lx) * 4;
                px[i + 3] = ScaleAlpha(px, i, (byte)(255 - coverage));
            }
        }
    }

    /// <summary>Alpha × factor / 255; a pixel that becomes fully transparent is zeroed.</summary>
    private static byte ScaleAlpha(Span<byte> px, int offset, byte factor)
    {
        int a = (px[offset + 3] * factor + 127) / 255;
        if (a == 0)
            px[offset] = px[offset + 1] = px[offset + 2] = 0;
        return (byte)a;
    }
}
