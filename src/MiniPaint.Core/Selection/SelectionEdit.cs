using MiniPaint.Core.Transform;

namespace MiniPaint.Core.Selection;

/// <summary>
/// Photoshop-style "Transform Selection": resize the selection rectangle only
/// (8 handles, opposite anchor by default, Alt = center, Shift = aspect ratio).
/// </summary>
public sealed class SelectionEditSession
{
    private readonly SelectionState _selection;
    public int OriginalX { get; }
    public int OriginalY { get; }
    public int OriginalWidth { get; }
    public int OriginalHeight { get; }

    public SelectionEditSession(SelectionState selection)
    {
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        if (!selection.HasSelection)
            throw new InvalidOperationException("No selection to edit.");
        OriginalX = selection.X;
        OriginalY = selection.Y;
        OriginalWidth = selection.Width;
        OriginalHeight = selection.Height;
    }

    public SelectionScaleInteraction BeginScale(TransformHandle handle) =>
        new(_selection, handle);

    public void RestoreOriginal() =>
        _selection.Set(OriginalX, OriginalY, OriginalWidth, OriginalHeight);

    public (double x, double y) GetHandleDocPoint(TransformHandle handle)
    {
        double l = _selection.X;
        double t = _selection.Y;
        double r = _selection.Right;
        double b = _selection.Bottom;
        double cx = l + _selection.Width / 2.0;
        double cy = t + _selection.Height / 2.0;
        return handle switch
        {
            TransformHandle.NW => (l, t),
            TransformHandle.N => (cx, t),
            TransformHandle.NE => (r, t),
            TransformHandle.E => (r, cy),
            TransformHandle.SE => (r, b),
            TransformHandle.S => (cx, b),
            TransformHandle.SW => (l, b),
            TransformHandle.W => (l, cy),
            TransformHandle.Move => (cx, cy),
            _ => (cx, cy)
        };
    }

    public TransformHandle HitTest(double docX, double docY, double handleRadiusDoc)
    {
        TransformHandle[] order =
        [
            TransformHandle.NW, TransformHandle.NE, TransformHandle.SE, TransformHandle.SW,
            TransformHandle.N, TransformHandle.E, TransformHandle.S, TransformHandle.W
        ];
        foreach (var h in order)
        {
            var (hx, hy) = GetHandleDocPoint(h);
            double dx = docX - hx;
            double dy = docY - hy;
            if (dx * dx + dy * dy <= handleRadiusDoc * handleRadiusDoc)
                return h;
        }

        if (_selection.ContainsDoc(docX, docY))
            return TransformHandle.Move;
        return TransformHandle.None;
    }
}

/// <summary>
/// Axis-aligned selection scale. Same anchor rules as Free Transform (no rotation).
/// Scale factors stay positive (selection rect does not flip).
/// </summary>
public sealed class SelectionScaleInteraction
{
    private readonly SelectionState _selection;
    private readonly double _startX, _startY, _startW, _startH;
    private readonly double _handleLocalX, _handleLocalY;
    private readonly double _oppLocalX, _oppLocalY;
    private readonly bool _lockX, _lockY;

    public SelectionScaleInteraction(SelectionState selection, TransformHandle handle)
    {
        _selection = selection;
        _startX = selection.X;
        _startY = selection.Y;
        _startW = selection.Width;
        _startH = selection.Height;

        double hw = _startW / 2.0;
        double hh = _startH / 2.0;
        (_handleLocalX, _handleLocalY) = HandleLocal(handle, hw, hh);
        (_oppLocalX, _oppLocalY) = OppositeLocal(handle, hw, hh);
        _lockX = handle is TransformHandle.N or TransformHandle.S;
        _lockY = handle is TransformHandle.E or TransformHandle.W;
    }

    public void Update(double docX, double docY, bool constrainProportions, bool fromCenter)
    {
        double ax = fromCenter ? 0 : _oppLocalX;
        double ay = fromCenter ? 0 : _oppLocalY;

        double cx0 = _startX + _startW / 2.0;
        double cy0 = _startY + _startH / 2.0;
        double anchorX = cx0 + ax;
        double anchorY = cy0 + ay;

        double vLocalX = docX - anchorX;
        double vLocalY = docY - anchorY;

        double spanX = _handleLocalX - ax;
        double spanY = _handleLocalY - ay;

        double sx = 1;
        double sy = 1;
        if (!_lockX && Math.Abs(spanX) > 1e-9)
            sx = vLocalX / spanX;
        if (!_lockY && Math.Abs(spanY) > 1e-9)
            sy = vLocalY / spanY;
        if (_lockX) sx = 1;
        if (_lockY) sy = 1;

        // Selection rect: keep positive scales (crossing opposite side shrinks toward min size).
        sx = Math.Max(0.001, sx);
        sy = Math.Max(0.001, sy);

        if (constrainProportions)
        {
            if (_lockX) sx = sy;
            else if (_lockY) sy = sx;
            else
            {
                double f = Math.Max(sx, sy);
                sx = f;
                sy = f;
            }
        }

        double newW = Math.Max(1.0, _startW * sx);
        double newH = Math.Max(1.0, _startH * sy);

        double fracAx = Math.Abs(_startW) < 1e-9 ? 0 : ax / (_startW / 2.0);
        double fracAy = Math.Abs(_startH) < 1e-9 ? 0 : ay / (_startH / 2.0);
        if (fromCenter) { fracAx = 0; fracAy = 0; }

        double newHalfW = newW / 2.0;
        double newHalfH = newH / 2.0;
        double centerX1 = anchorX - fracAx * newHalfW;
        double centerY1 = anchorY - fracAy * newHalfH;

        int ix = (int)Math.Round(centerX1 - newHalfW);
        int iy = (int)Math.Round(centerY1 - newHalfH);
        int iw = Math.Max(1, (int)Math.Round(newW));
        int ih = Math.Max(1, (int)Math.Round(newH));
        _selection.Set(ix, iy, iw, ih);
    }

    private static (double x, double y) HandleLocal(TransformHandle handle, double hw, double hh) => handle switch
    {
        TransformHandle.NW => (-hw, -hh),
        TransformHandle.N => (0, -hh),
        TransformHandle.NE => (hw, -hh),
        TransformHandle.E => (hw, 0),
        TransformHandle.SE => (hw, hh),
        TransformHandle.S => (0, hh),
        TransformHandle.SW => (-hw, hh),
        TransformHandle.W => (-hw, 0),
        _ => (0, 0)
    };

    private static (double x, double y) OppositeLocal(TransformHandle handle, double hw, double hh) => handle switch
    {
        TransformHandle.NW => (hw, hh),
        TransformHandle.N => (0, hh),
        TransformHandle.NE => (-hw, hh),
        TransformHandle.E => (-hw, 0),
        TransformHandle.SE => (-hw, -hh),
        TransformHandle.S => (0, -hh),
        TransformHandle.SW => (hw, -hh),
        TransformHandle.W => (hw, 0),
        _ => (0, 0)
    };
}
