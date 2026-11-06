namespace MiniPaint.Core.Selection;

/// <summary>Axis-aligned outline segment along pixel edges, in document coordinates.</summary>
public readonly record struct ContourSegment(int X0, int Y0, int X1, int Y1);

/// <summary>
/// Marching-ants outline of a selection: every pixel edge between "inside" (coverage ≥ 50%)
/// and "outside", with consecutive edges on the same line merged into one segment.
/// </summary>
public static class SelectionContour
{
    /// <param name="cellSize">
    /// Trace on cells of cellSize × cellSize pixels (a cell is inside when any of its pixels is).
    /// Used when zoomed out: one cell per screen pixel keeps fragmented selections cheap to draw.
    /// </param>
    public static IReadOnlyList<ContourSegment> Build(SelectionState selection, int cellSize = 1)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var segments = new List<ContourSegment>();
        if (!selection.HasSelection) return segments;

        int ox = selection.X;
        int oy = selection.Y;
        int w = selection.Width;
        int h = selection.Height;

        if (selection.IsRectangular)
        {
            segments.Add(new(ox, oy, ox + w, oy));
            segments.Add(new(ox + w, oy, ox + w, oy + h));
            segments.Add(new(ox, oy + h, ox + w, oy + h));
            segments.Add(new(ox, oy, ox, oy + h));
            return segments;
        }

        int cs = Math.Max(1, cellSize);
        int gw = (w + cs - 1) / cs;
        int gh = (h + cs - 1) / cs;
        var inside = new byte[gw * gh];
        var mask = selection.Mask;
        for (int y = 0; y < h; y++)
        {
            var row = mask.Slice(y * w, w);
            int cellRow = y / cs * gw;
            for (int x = 0; x < w; x++)
            {
                if (row[x] >= SelectionState.OutlineThreshold)
                    inside[cellRow + x / cs] = 1;
            }
        }

        // Grid line → document coordinate (the last cell may be partial).
        int DocX(int gx) => ox + Math.Min(gx * cs, w);
        int DocY(int gy) => oy + Math.Min(gy * cs, h);

        // Horizontal edges: line gy separates cell row gy-1 (above) from gy (below).
        for (int gy = 0; gy <= gh; gy++)
        {
            int runStart = -1;
            for (int gx = 0; gx <= gw; gx++)
            {
                bool edge = gx < gw && Inside(inside, gw, gh, gx, gy - 1) != Inside(inside, gw, gh, gx, gy);
                if (edge && runStart < 0)
                    runStart = gx;
                else if (!edge && runStart >= 0)
                {
                    segments.Add(new(DocX(runStart), DocY(gy), DocX(gx), DocY(gy)));
                    runStart = -1;
                }
            }
        }

        // Vertical edges: line gx separates cell column gx-1 (left) from gx (right).
        for (int gx = 0; gx <= gw; gx++)
        {
            int runStart = -1;
            for (int gy = 0; gy <= gh; gy++)
            {
                bool edge = gy < gh && Inside(inside, gw, gh, gx - 1, gy) != Inside(inside, gw, gh, gx, gy);
                if (edge && runStart < 0)
                    runStart = gy;
                else if (!edge && runStart >= 0)
                {
                    segments.Add(new(DocX(gx), DocY(runStart), DocX(gx), DocY(gy)));
                    runStart = -1;
                }
            }
        }

        return segments;
    }

    private static bool Inside(byte[] cells, int gw, int gh, int x, int y) =>
        (uint)x < (uint)gw && (uint)y < (uint)gh && cells[y * gw + x] != 0;
}
