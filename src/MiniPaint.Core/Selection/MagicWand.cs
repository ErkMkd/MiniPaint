using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Selection;

/// <param name="Tolerance">
/// Largest per-channel difference (0–255) from the clicked color: 0 = that exact color only.
/// </param>
/// <param name="Contiguous">Only pixels connected to the clicked one (4-neighbour); otherwise the whole image.</param>
/// <param name="AntiAlias">Soften the selection edge by one pixel (photos); off keeps hard pixel edges.</param>
/// <param name="SampleVisibleImage">
/// Sample the visible image (all layers, image mode applied — exact palette colors in Indexed / ECS / AGA);
/// otherwise the active layer only.
/// </param>
public readonly record struct MagicWandOptions(
    int Tolerance = 32,
    bool Contiguous = true,
    bool AntiAlias = false,
    bool SampleVisibleImage = true);

/// <summary>
/// Magic Wand: select pixels whose color is close to the clicked pixel. Colors are compared
/// premultiplied by alpha, so all fully transparent pixels count as one color.
/// </summary>
public static class MagicWand
{
    /// <summary>Map value of a pixel whose color matches but is not (yet) reached by the fill.</summary>
    private const byte Candidate = 1;

    /// <summary>Selection of matching pixels in <paramref name="source"/> (document-sized RGBA).</summary>
    public static SelectionState Select(PixelBuffer source, int seedX, int seedY, MagicWandOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new SelectionState();
        int w = source.Width;
        int h = source.Height;
        if ((uint)seedX >= (uint)w || (uint)seedY >= (uint)h) return result;

        var seed = ColorMetric.Premultiplied(source.PixelsReadOnly, (seedY * w + seedX) * 4);
        int tolerance = Math.Clamp(options.Tolerance, 0, 255);

        // Color test for every pixel first (parallel); the fill then only compares bytes.
        var mask = MatchMap(source, seed, tolerance, options.Contiguous ? Candidate : (byte)255);
        if (options.Contiguous)
        {
            FloodFill(mask, w, h, seedX, seedY);
            mask.AsSpan().Replace(Candidate, (byte)0); // matching but not connected
        }

        if (options.AntiAlias)
            SmoothEdges(mask, w, h);

        result.SetMask(0, 0, w, h, mask);
        return result;
    }

    private static byte[] MatchMap(PixelBuffer source, (int r, int g, int b, int a) seed, int tolerance, byte value)
    {
        int w = source.Width;
        var map = new byte[w * source.Height];
        Parallel.For(0, source.Height, y =>
        {
            var px = source.PixelsReadOnly.Slice(y * w * 4, w * 4);
            var row = map.AsSpan(y * w, w);
            for (int x = 0; x < w; x++)
            {
                if (Matches(px, x * 4, seed, tolerance))
                    row[x] = value;
            }
        });
        return map;
    }

    /// <summary>
    /// Scanline fill over the match map from the seed: each popped point grows into a horizontal
    /// span of candidates (set to 255), then one seed is pushed per candidate run above and below.
    /// </summary>
    private static void FloodFill(byte[] map, int w, int h, int seedX, int seedY)
    {
        var stack = new Stack<(int x, int y)>();
        stack.Push((seedX, seedY));
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            int row = y * w;
            if (map[row + x] != Candidate) continue;

            int left = map.AsSpan(row, x + 1).LastIndexOfAnyExcept(Candidate);
            int x0 = left + 1;
            int right = map.AsSpan(row + x, w - x).IndexOfAnyExcept(Candidate);
            int x1 = right < 0 ? w - 1 : x + right - 1;
            map.AsSpan(row + x0, x1 - x0 + 1).Fill(255);

            for (int ny = y - 1; ny <= y + 1; ny += 2)
            {
                if ((uint)ny >= (uint)h) continue;
                var span = map.AsSpan(ny * w + x0, x1 - x0 + 1);
                int i = 0;
                while (i < span.Length)
                {
                    int start = span[i..].IndexOf(Candidate);
                    if (start < 0) break;
                    start += i;
                    stack.Push((x0 + start, ny));
                    int end = span[start..].IndexOfAnyExcept(Candidate);
                    if (end < 0) break;
                    i = start + end;
                }
            }
        }
    }

    private static bool Matches(ReadOnlySpan<byte> px, int offset, (int r, int g, int b, int a) seed, int tolerance)
    {
        var c = ColorMetric.Premultiplied(px, offset);
        return Math.Abs(c.r - seed.r) <= tolerance &&
               Math.Abs(c.g - seed.g) <= tolerance &&
               Math.Abs(c.b - seed.b) <= tolerance &&
               Math.Abs(c.a - seed.a) <= tolerance;
    }

    /// <summary>
    /// 3×3 box blur of the mask around the selected area (edges of the image are extended,
    /// so a selection touching the border stays fully selected there).
    /// </summary>
    private static void SmoothEdges(byte[] mask, int w, int h)
    {
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            var row = mask.AsSpan(y * w, w);
            int first = row.IndexOfAnyExcept((byte)0);
            if (first < 0) continue;
            minX = Math.Min(minX, first);
            maxX = Math.Max(maxX, row.LastIndexOfAnyExcept((byte)0));
            if (y < minY) minY = y;
            maxY = y;
        }
        if (maxX < 0) return;

        int x0 = Math.Max(0, minX - 1), x1 = Math.Min(w - 1, maxX + 1);
        int y0 = Math.Max(0, minY - 1), y1 = Math.Min(h - 1, maxY + 1);
        int bw = x1 - x0 + 1;

        // Horizontal sums over the window extended by one row above/below for the vertical pass.
        int sy0 = Math.Max(0, y0 - 1), sy1 = Math.Min(h - 1, y1 + 1);
        var rows = new int[(sy1 - sy0 + 1) * bw];
        Parallel.For(sy0, sy1 + 1, y =>
        {
            int row = y * w;
            int dst = (y - sy0) * bw;
            for (int x = x0; x <= x1; x++)
            {
                int left = mask[row + Math.Max(0, x - 1)];
                int right = mask[row + Math.Min(w - 1, x + 1)];
                rows[dst + x - x0] = left + mask[row + x] + right;
            }
        });

        Parallel.For(y0, y1 + 1, y =>
        {
            int up = (Math.Max(0, y - 1) - sy0) * bw;
            int mid = (y - sy0) * bw;
            int down = (Math.Min(h - 1, y + 1) - sy0) * bw;
            int row = y * w;
            for (int i = 0; i < bw; i++)
                mask[row + x0 + i] = (byte)((rows[up + i] + rows[mid + i] + rows[down + i] + 4) / 9);
        });
    }
}
