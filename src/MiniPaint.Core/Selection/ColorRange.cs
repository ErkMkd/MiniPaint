using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Selection;

/// <summary>A color picked from the image (straight RGBA).</summary>
public readonly record struct SampledColor(byte R, byte G, byte B, byte A);

/// <param name="Include">Colors to select (Shift+click adds one).</param>
/// <param name="Exclude">Colors to keep out (Alt+click), even when close to an included one.</param>
/// <param name="Fuzziness">
/// 0–255. Pixels within half of it (largest channel difference) are fully selected, then the
/// selection fades out to nothing at the full value; 0 selects the exact colors only.
/// </param>
public sealed record ColorRangeOptions(
    IReadOnlyList<SampledColor> Include,
    IReadOnlyList<SampledColor> Exclude,
    int Fuzziness);

/// <summary>
/// Select → Color Range: every pixel of the image whose color is close to one of the sampled
/// colors, with soft coverage controlled by fuzziness (not limited to connected areas).
/// </summary>
public static class ColorRange
{
    public static SampledColor SampleAt(PixelBuffer source, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(source);
        x = Math.Clamp(x, 0, source.Width - 1);
        y = Math.Clamp(y, 0, source.Height - 1);
        var px = source.PixelsReadOnly;
        int i = source.Offset(x, y);
        return new SampledColor(px[i], px[i + 1], px[i + 2], px[i + 3]);
    }

    /// <summary>Coverage 0–255 for every pixel of <paramref name="source"/> (row-major).</summary>
    public static byte[] Coverage(PixelBuffer source, ColorRangeOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        int w = source.Width;
        var coverage = new byte[w * source.Height];
        if (options.Include.Count == 0) return coverage;

        var include = options.Include.Select(c => ColorMetric.Premultiplied(c.R, c.G, c.B, c.A)).ToArray();
        var exclude = options.Exclude.Select(c => ColorMetric.Premultiplied(c.R, c.G, c.B, c.A)).ToArray();
        int fuzziness = Math.Clamp(options.Fuzziness, 0, 255);
        var falloff = new byte[256];
        for (int d = 0; d < 256; d++)
            falloff[d] = CoverageForDistance(d, fuzziness);

        Parallel.For(0, source.Height, y =>
        {
            var px = source.PixelsReadOnly.Slice(y * w * 4, w * 4);
            var row = coverage.AsSpan(y * w, w);
            for (int x = 0; x < w; x++)
            {
                var c = ColorMetric.Premultiplied(px, x * 4);
                int selected = 0;
                foreach (var s in include)
                    selected = Math.Max(selected, falloff[ColorMetric.MaxDifference(c, s)]);
                if (selected == 0) continue;

                int excluded = 0;
                foreach (var s in exclude)
                    excluded = Math.Max(excluded, falloff[ColorMetric.MaxDifference(c, s)]);
                row[x] = (byte)((selected * (255 - excluded) + 127) / 255);
            }
        });
        return coverage;
    }

    /// <summary>Selection over the whole document (<paramref name="source"/> is document-sized).</summary>
    public static SelectionState Select(PixelBuffer source, ColorRangeOptions options)
    {
        var selection = new SelectionState();
        selection.SetMask(0, 0, source.Width, source.Height, Coverage(source, options));
        return selection;
    }

    /// <summary>255 up to half the fuzziness, then a linear fade to 0 at the fuzziness.</summary>
    public static byte CoverageForDistance(int distance, int fuzziness)
    {
        if (distance * 2 <= fuzziness) return 255;
        if (distance >= fuzziness) return 0;
        double half = fuzziness / 2.0;
        return (byte)Math.Round(255 * (fuzziness - distance) / (fuzziness - half));
    }
}
