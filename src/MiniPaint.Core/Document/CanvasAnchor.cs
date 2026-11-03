namespace MiniPaint.Core.Document;

/// <summary>
/// Photoshop-style 9-point anchor for canvas resize (content stays fixed relative to this side).
/// </summary>
public enum CanvasAnchor
{
    NorthWest,
    North,
    NorthEast,
    West,
    Center,
    East,
    SouthWest,
    South,
    SouthEast
}

public static class CanvasAnchorMath
{
    /// <summary>
    /// Shift to add to layer offsets when the canvas size changes so content stays on the anchor.
    /// </summary>
    public static (int dx, int dy) OffsetDelta(CanvasAnchor anchor, int oldWidth, int oldHeight, int newWidth, int newHeight)
    {
        int dW = newWidth - oldWidth;
        int dH = newHeight - oldHeight;
        return (AxisShift(Horizontal(anchor), dW), AxisShift(Vertical(anchor), dH));
    }

    /// <summary>0 = min edge, 1 = center, 2 = max edge.</summary>
    public static int Horizontal(CanvasAnchor a) => a switch
    {
        CanvasAnchor.NorthWest or CanvasAnchor.West or CanvasAnchor.SouthWest => 0,
        CanvasAnchor.North or CanvasAnchor.Center or CanvasAnchor.South => 1,
        _ => 2
    };

    public static int Vertical(CanvasAnchor a) => a switch
    {
        CanvasAnchor.NorthWest or CanvasAnchor.North or CanvasAnchor.NorthEast => 0,
        CanvasAnchor.West or CanvasAnchor.Center or CanvasAnchor.East => 1,
        _ => 2
    };

    private static int AxisShift(int pos, int delta) => pos switch
    {
        0 => 0,
        1 => delta / 2,
        _ => delta
    };
}
