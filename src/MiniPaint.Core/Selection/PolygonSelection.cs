namespace MiniPaint.Core.Selection;

/// <summary>
/// Polygonal Lasso: the selection inside a closed polygon. Corners are in document coordinates
/// (pixel i spans [i, i+1)); the last corner joins the first. Everything the outline encloses is
/// selected, loops crossing themselves included (non-zero winding).
/// </summary>
public static class PolygonSelection
{
    /// <summary>Sub-scanlines per pixel row when anti-aliasing (horizontal coverage is exact).</summary>
    private const int SubRows = 4;

    /// <summary>
    /// Coverage of the polygon, clipped to the document. Anti-aliased edges get partial coverage;
    /// without it a pixel is selected when its center is inside. Fewer than 3 corners: empty.
    /// </summary>
    public static SelectionState Rasterize(IReadOnlyList<(double X, double Y)> corners, bool antiAlias, int docWidth, int docHeight)
    {
        ArgumentNullException.ThrowIfNull(corners);
        var selection = new SelectionState();
        if (corners.Count < 3) return selection;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (x, y) in corners)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }
        int x0 = Math.Max(0, (int)Math.Floor(minX));
        int y0 = Math.Max(0, (int)Math.Floor(minY));
        int x1 = Math.Min(docWidth, (int)Math.Ceiling(maxX));
        int y1 = Math.Min(docHeight, (int)Math.Ceiling(maxY));
        if (x1 <= x0 || y1 <= y0) return selection;

        int width = x1 - x0;
        int height = y1 - y0;
        int samples = antiAlias ? SubRows : 1;
        var mask = new byte[width * height];
        Parallel.For(0, height, () => (Cover: new float[width], Crossings: new List<(double X, int Dir)>()), (row, _, scratch) =>
        {
            var (cover, crossings) = scratch;
            Array.Clear(cover);
            for (int s = 0; s < samples; s++)
            {
                double sy = y0 + row + (s + 0.5) / samples;
                Crossings(corners, sy, crossings);
                int winding = 0;
                double spanStart = 0;
                foreach (var (x, dir) in crossings)
                {
                    int before = winding;
                    winding += dir;
                    if (before == 0 && winding != 0)
                        spanStart = x;
                    else if (before != 0 && winding == 0)
                        AddSpan(cover, x0, spanStart, x, antiAlias);
                }
            }
            var dst = mask.AsSpan(row * width, width);
            for (int i = 0; i < width; i++)
                dst[i] = (byte)Math.Round(Math.Min(1f, cover[i] / samples) * 255);
            return scratch;
        }, _ => { });

        selection.SetMask(x0, y0, width, height, mask);
        return selection;
    }

    /// <summary>Shift while placing a corner: the new side snaps to the nearest multiple of 45°.</summary>
    public static (double X, double Y) Constrain45((double X, double Y) from, (double X, double Y) to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length == 0) return to;
        double step = Math.PI / 4;
        double angle = Math.Round(Math.Atan2(dy, dx) / step) * step;
        return (from.X + length * Math.Cos(angle), from.Y + length * Math.Sin(angle));
    }

    /// <summary>Where the outline crosses the scanline y, with its direction (+1 down, −1 up), left to right.</summary>
    private static void Crossings(IReadOnlyList<(double X, double Y)> corners, double y, List<(double X, int Dir)> result)
    {
        result.Clear();
        for (int i = 0; i < corners.Count; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % corners.Count];
            if (a.Y == b.Y) continue;
            // Half-open in y, so a corner shared by two sides counts once.
            if (y < Math.Min(a.Y, b.Y) || y >= Math.Max(a.Y, b.Y)) continue;
            double x = a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
            result.Add((x, a.Y < b.Y ? 1 : -1));
        }
        result.Sort((p, q) => p.X.CompareTo(q.X));
    }

    /// <summary>Add the span [from, to) of one scanline to the row coverage (cover[0] is document x = x0).</summary>
    private static void AddSpan(float[] cover, int x0, double from, double to, bool antiAlias)
    {
        int width = cover.Length;
        if (antiAlias)
        {
            int first = Math.Max(0, (int)Math.Floor(from) - x0);
            int last = Math.Min(width - 1, (int)Math.Ceiling(to) - 1 - x0);
            for (int i = first; i <= last; i++)
            {
                double left = Math.Max(from, x0 + i);
                double right = Math.Min(to, x0 + i + 1);
                if (right > left)
                    cover[i] += (float)(right - left);
            }
            return;
        }

        // Pixel centers inside the span: from ≤ x + 0.5 < to.
        int start = Math.Max(0, (int)Math.Ceiling(from - 0.5) - x0);
        int end = Math.Min(width, (int)Math.Ceiling(to - 0.5) - x0);
        for (int i = start; i < end; i++)
            cover[i] += 1;
    }
}
