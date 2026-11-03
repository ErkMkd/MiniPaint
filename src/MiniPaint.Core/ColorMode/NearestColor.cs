namespace MiniPaint.Core.ColorMode;

internal static class NearestColor
{
    public static int Find(IReadOnlyList<PaletteRgb> palette, byte r, byte g, byte b)
    {
        int best = 0;
        int bestDist = int.MaxValue;
        for (int i = 0; i < palette.Count; i++)
        {
            int d = palette[i].DistanceSquared(r, g, b);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
                if (d == 0) break;
            }
        }
        return best;
    }
}
