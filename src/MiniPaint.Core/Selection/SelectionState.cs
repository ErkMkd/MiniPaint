namespace MiniPaint.Core.Selection;

/// <summary>
/// Axis-aligned rectangular selection in document coordinates.
/// Empty when HasSelection is false.
/// </summary>
public sealed class SelectionState
{
    public bool HasSelection { get; private set; }
    public int X { get; private set; }
    public int Y { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public int Right => X + Width;
    public int Bottom => Y + Height;

    public void Set(int x, int y, int width, int height)
    {
        if (width < 0)
        {
            x += width;
            width = -width;
        }
        if (height < 0)
        {
            y += height;
            height = -height;
        }

        if (width == 0 || height == 0)
        {
            Clear();
            return;
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
        HasSelection = true;
    }

    public void ClampToDocument(int docWidth, int docHeight)
    {
        if (!HasSelection) return;

        int x0 = Math.Clamp(X, 0, docWidth);
        int y0 = Math.Clamp(Y, 0, docHeight);
        int x1 = Math.Clamp(Right, 0, docWidth);
        int y1 = Math.Clamp(Bottom, 0, docHeight);
        int w = x1 - x0;
        int h = y1 - y0;
        if (w <= 0 || h <= 0)
        {
            Clear();
            return;
        }

        X = x0;
        Y = y0;
        Width = w;
        Height = h;
    }

    public void SelectAll(int docWidth, int docHeight) => Set(0, 0, docWidth, docHeight);

    public void Clear()
    {
        HasSelection = false;
        X = Y = Width = Height = 0;
    }
}
