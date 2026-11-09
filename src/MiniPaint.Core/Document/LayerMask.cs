using MiniPaint.Core.Imaging;
using MiniPaint.Core.Selection;

namespace MiniPaint.Core.Document;

/// <summary>Helpers for layer masks: one byte per layer pixel, 255 shows it, 0 hides it.</summary>
public static class LayerMask
{
    public const byte Reveal = 255;
    public const byte Hide = 0;

    public static byte[] Filled(int width, int height, byte value)
    {
        var mask = new byte[width * height];
        if (value != 0)
            Array.Fill(mask, value);
        return mask;
    }

    /// <summary>Mask that shows the selected part of <paramref name="layer"/> (selection coverage at its pixels).</summary>
    public static byte[] FromSelection(SelectionState selection, Layer layer)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(layer);
        int w = layer.Pixels.Width;
        int h = layer.Pixels.Height;
        var mask = new byte[w * h];
        if (!selection.HasSelection) return mask;

        int x0 = Math.Max(0, selection.X - layer.OffsetX);
        int x1 = Math.Min(w, selection.Right - layer.OffsetX);
        int y0 = Math.Max(0, selection.Y - layer.OffsetY);
        int y1 = Math.Min(h, selection.Bottom - layer.OffsetY);
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
                mask[y * w + x] = selection.CoverageAt(x + layer.OffsetX, y + layer.OffsetY);
        }
        return mask;
    }

    /// <summary>Resize a mask with the image resize filter (Mitchell cubic).</summary>
    public static byte[] Resample(byte[] mask, int width, int height, int newWidth, int newHeight)
    {
        ArgumentNullException.ThrowIfNull(mask);
        using var buffer = ToBuffer(mask, width, height);
        using var scaled = PixelOps.ScaleBuffer(buffer, newWidth, newHeight);
        return FromAlpha(scaled);
    }

    /// <summary>White pixels whose alpha is the mask, for the image operations (transform, resize).</summary>
    public static PixelBuffer ToBuffer(byte[] mask, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ThrowIfWrongSize(mask, width, height);
        var buffer = new PixelBuffer(width, height);
        var px = buffer.Pixels;
        for (int i = 0; i < mask.Length; i++)
        {
            int o = i * 4;
            px[o] = px[o + 1] = px[o + 2] = 255;
            px[o + 3] = mask[i];
        }
        return buffer;
    }

    public static byte[] FromAlpha(PixelBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var px = buffer.PixelsReadOnly;
        var mask = new byte[buffer.Width * buffer.Height];
        for (int i = 0; i < mask.Length; i++)
            mask[i] = px[i * 4 + 3];
        return mask;
    }

    /// <summary>Bake the mask into the pixels' alpha (Apply Layer Mask).</summary>
    public static void ApplyTo(PixelBuffer pixels, ReadOnlySpan<byte> mask)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (mask.Length != pixels.Width * pixels.Height)
            throw new ArgumentException("The mask does not match the pixels.", nameof(mask));
        var px = pixels.Pixels;
        for (int i = 0; i < mask.Length; i++)
        {
            int o = i * 4;
            int a = (px[o + 3] * mask[i] + 127) / 255;
            if (a == 0)
                px.Slice(o, 4).Clear();
            else
                px[o + 3] = (byte)a;
        }
    }

    internal static void ThrowIfWrongSize(byte[]? mask, int width, int height)
    {
        if (mask is not null && mask.Length != width * height)
            throw new ArgumentException($"A {width}×{height} layer needs {width * height} mask values, not {mask.Length}.", nameof(mask));
    }
}
