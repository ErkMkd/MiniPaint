using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.ColorMode;

internal static class PixelSampler
{
    /// <summary>Collect opaque pixels (optionally snapped), capped for heavy algorithms.</summary>
    public static List<(byte r, byte g, byte b)> CollectOpaque(
        PixelBuffer source, int bitsPerChannel, int maxSamples = 0)
    {
        var px = source.PixelsReadOnly;
        int total = source.Width * source.Height;
        var list = new List<(byte, byte, byte)>(maxSamples > 0 ? Math.Min(maxSamples, total) : Math.Min(total, 65536));

        if (maxSamples <= 0 || maxSamples >= total)
        {
            for (int i = 0; i < px.Length; i += 4)
            {
                if (px[i + 3] == 0) continue;
                var c = AmigaColorSpace.SnapColor(px[i], px[i + 1], px[i + 2], bitsPerChannel);
                list.Add((c.R, c.G, c.B));
            }
            return list;
        }

        // Strided sampling for large images
        double step = (double)total / maxSamples;
        double cursor = 0;
        int taken = 0;
        while (taken < maxSamples && cursor < total)
        {
            int idx = (int)cursor;
            int i = idx * 4;
            if (px[i + 3] != 0)
            {
                var c = AmigaColorSpace.SnapColor(px[i], px[i + 1], px[i + 2], bitsPerChannel);
                list.Add((c.R, c.G, c.B));
                taken++;
            }
            cursor += step;
        }

        if (list.Count == 0)
            list.Add((0, 0, 0));
        return list;
    }
}
