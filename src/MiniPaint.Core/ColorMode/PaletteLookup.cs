namespace MiniPaint.Core.ColorMode;

/// <summary>
/// Exact nearest-palette-color search for per-pixel remapping. The RGB cube is split into
/// 16×16×16 cells; each cell keeps only the palette entries that can be nearest to some color
/// inside it (an entry whose closest possible distance to the cell exceeds the smallest
/// farthest-distance of all entries can never win). Same result and tie-break (lowest index)
/// as <see cref="NearestColor.Find"/>, with a handful of comparisons instead of the whole palette.
/// Safe to use from several threads.
/// </summary>
internal sealed class PaletteLookup
{
    private const int CellShift = 4;            // 16 levels per cell edge
    private const int CellsPerAxis = 256 >> CellShift;

    private readonly int[] _r;
    private readonly int[] _g;
    private readonly int[] _b;
    private readonly int[]?[] _cells = new int[CellsPerAxis * CellsPerAxis * CellsPerAxis][];

    public IReadOnlyList<PaletteRgb> Palette { get; }

    public PaletteLookup(IReadOnlyList<PaletteRgb> palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        Palette = palette;
        _r = new int[palette.Count];
        _g = new int[palette.Count];
        _b = new int[palette.Count];
        for (int i = 0; i < palette.Count; i++)
        {
            _r[i] = palette[i].R;
            _g[i] = palette[i].G;
            _b[i] = palette[i].B;
        }
    }

    public int Find(byte r, byte g, byte b)
    {
        if (_r.Length == 0) return 0;

        int cell = ((r >> CellShift) * CellsPerAxis + (g >> CellShift)) * CellsPerAxis + (b >> CellShift);
        // Built lazily; a race only builds the same list twice.
        var candidates = _cells[cell] ??= BuildCell(r >> CellShift, g >> CellShift, b >> CellShift);

        int best = 0;
        int bestDist = int.MaxValue;
        foreach (int i in candidates)
        {
            int dr = _r[i] - r;
            int dg = _g[i] - g;
            int db = _b[i] - b;
            int d = dr * dr + dg * dg + db * db;
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
                if (d == 0) break;
            }
        }
        return best;
    }

    /// <summary>Candidates of one cell, in ascending palette order (keeps the linear-scan tie-break).</summary>
    private int[] BuildCell(int cr, int cg, int cb)
    {
        int n = _r.Length;
        var minDist = new int[n];
        int bestMax = int.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var (minR, maxR) = AxisRange(_r[i], cr);
            var (minG, maxG) = AxisRange(_g[i], cg);
            var (minB, maxB) = AxisRange(_b[i], cb);
            minDist[i] = minR + minG + minB;
            bestMax = Math.Min(bestMax, maxR + maxG + maxB);
        }

        var candidates = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (minDist[i] <= bestMax)
                candidates.Add(i);
        }
        return candidates.ToArray();
    }

    /// <summary>Squared distance range from palette value <paramref name="v"/> to the cell's values on one axis.</summary>
    private static (int min, int max) AxisRange(int v, int cell)
    {
        int lo = cell << CellShift;
        int hi = lo + (1 << CellShift) - 1;
        int min = v < lo ? lo - v : v > hi ? v - hi : 0;
        int max = Math.Max(Math.Abs(v - lo), Math.Abs(v - hi));
        return (min * min, max * max);
    }
}
