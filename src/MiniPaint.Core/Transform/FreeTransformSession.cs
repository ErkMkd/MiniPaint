using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;
using SkiaSharp;

namespace MiniPaint.Core.Transform;

public enum TransformHandle
{
    None,
    Move,
    Rotate,
    N, S, E, W,
    NE, NW, SE, SW
}

/// <summary>
/// Photoshop-style free transform for one layer: live scale/rotate/move from a frozen original.
/// Shift = constrain proportions (scale) / 15° snaps (rotate). Alt = scale from center.
/// </summary>
public sealed class FreeTransformSession : IDisposable
{
    private bool _disposed;

    public Layer Layer { get; }
    public PixelBuffer OriginalPixels { get; }
    public int OriginalOffsetX { get; }
    public int OriginalOffsetY { get; }

    /// <summary>Pivot in document space (layer center at session start, then follows translation).</summary>
    public double PivotX { get; private set; }
    public double PivotY { get; private set; }

    public double ScaleX { get; set; } = 1.0;
    public double ScaleY { get; set; } = 1.0;
    public double RotationDegrees { get; set; }
    public double TranslateX { get; set; }
    public double TranslateY { get; set; }

    public int BaseWidth => OriginalPixels.Width;
    public int BaseHeight => OriginalPixels.Height;

    public FreeTransformSession(Layer layer)
    {
        Layer = layer ?? throw new ArgumentNullException(nameof(layer));
        OriginalPixels = layer.Pixels.Clone();
        OriginalOffsetX = layer.OffsetX;
        OriginalOffsetY = layer.OffsetY;
        PivotX = OriginalOffsetX + OriginalPixels.Width / 2.0;
        PivotY = OriginalOffsetY + OriginalPixels.Height / 2.0;
    }

    public double RotationRadians => RotationDegrees * Math.PI / 180.0;

    /// <summary>Four corners of the transformed quad in document space (TL, TR, BR, BL).</summary>
    public (double x, double y)[] GetCorners()
    {
        double hw = BaseWidth / 2.0;
        double hh = BaseHeight / 2.0;
        var local = new (double x, double y)[]
        {
            (-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh)
        };

        double cos = Math.Cos(RotationRadians);
        double sin = Math.Sin(RotationRadians);
        var result = new (double x, double y)[4];
        for (int i = 0; i < 4; i++)
        {
            double sx = local[i].x * ScaleX;
            double sy = local[i].y * ScaleY;
            double rx = sx * cos - sy * sin;
            double ry = sx * sin + sy * cos;
            result[i] = (PivotX + TranslateX + rx, PivotY + TranslateY + ry);
        }
        return result;
    }

    public (double x, double y) GetHandleDocPoint(TransformHandle handle)
    {
        var c = GetCorners();
        return handle switch
        {
            TransformHandle.NW => c[0],
            TransformHandle.NE => c[1],
            TransformHandle.SE => c[2],
            TransformHandle.SW => c[3],
            TransformHandle.N => Mid(c[0], c[1]),
            TransformHandle.E => Mid(c[1], c[2]),
            TransformHandle.S => Mid(c[2], c[3]),
            TransformHandle.W => Mid(c[3], c[0]),
            TransformHandle.Move => (PivotX + TranslateX, PivotY + TranslateY),
            _ => (PivotX + TranslateX, PivotY + TranslateY)
        };
    }

    private static (double x, double y) Mid((double x, double y) a, (double x, double y) b) =>
        ((a.x + b.x) / 2, (a.y + b.y) / 2);

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

        // Rotate zone: near a corner but outside the quad a bit — checked by caller with screen distance.
        if (PointInQuad(docX, docY, GetCorners()))
            return TransformHandle.Move;

        return TransformHandle.None;
    }

    public bool HitTestRotate(double docX, double docY, double innerRadius, double outerRadius)
    {
        // Near any corner, outside the shape
        TransformHandle[] corners =
            [TransformHandle.NW, TransformHandle.NE, TransformHandle.SE, TransformHandle.SW];
        foreach (var h in corners)
        {
            var (hx, hy) = GetHandleDocPoint(h);
            double d = Math.Sqrt((docX - hx) * (docX - hx) + (docY - hy) * (docY - hy));
            if (d > innerRadius && d < outerRadius)
                return true;
        }
        return false;
    }

    public static bool PointInQuad(double x, double y, (double x, double y)[] q)
    {
        // Barycentric / cross-product winding for convex quad
        bool Cross((double x, double y) a, (double x, double y) b, (double x, double y) p) =>
            (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x) >= 0;

        var p = (x, y);
        bool s0 = Cross(q[0], q[1], p);
        bool s1 = Cross(q[1], q[2], p);
        bool s2 = Cross(q[2], q[3], p);
        bool s3 = Cross(q[3], q[0], p);
        return s0 == s1 && s1 == s2 && s2 == s3;
    }

    /// <summary>
    /// Begin a scale interaction from a handle. Returns interaction state.
    /// </summary>
    public ScaleInteraction BeginScale(TransformHandle handle, double docX, double docY, bool fromCenter)
    {
        return new ScaleInteraction(this, handle, docX, docY, fromCenter);
    }

    public RotateInteraction BeginRotate(double docX, double docY) =>
        new(this, docX, docY);

    public MoveInteraction BeginMove(double docX, double docY) =>
        new(this, docX, docY);

    /// <summary>
    /// Bake transform into a new pixel buffer + document offset (AABB of result).
    /// </summary>
    public (PixelBuffer pixels, int offsetX, int offsetY) Bake()
    {
        var corners = GetCorners();
        double minX = corners.Min(c => c.x);
        double minY = corners.Min(c => c.y);
        double maxX = corners.Max(c => c.x);
        double maxY = corners.Max(c => c.y);

        int outW = Math.Max(1, (int)Math.Ceiling(maxX - minX) + 1);
        int outH = Math.Max(1, (int)Math.Ceiling(maxY - minY) + 1);
        int offsetX = (int)Math.Floor(minX);
        int offsetY = (int)Math.Floor(minY);

        using var srcBmp = ImageCodec.ToSkBitmap(OriginalPixels);
        var info = new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var dstBmp = new SKBitmap(info);
        dstBmp.Erase(SKColors.Transparent);

        using (var canvas = new SKCanvas(dstBmp))
        {
            // Map layer-local (0..w, 0..h) → document → output
            // Document point = T * R * S * (local - centerLocal) + pivot + translate
            // Output = document - (offsetX, offsetY)
            double cx = BaseWidth / 2.0;
            double cy = BaseHeight / 2.0;

            var matrix = SKMatrix.CreateIdentity();
            matrix = matrix.PostConcat(SKMatrix.CreateTranslation((float)-cx, (float)-cy));
            matrix = matrix.PostConcat(SKMatrix.CreateScale((float)ScaleX, (float)ScaleY));
            matrix = matrix.PostConcat(SKMatrix.CreateRotationDegrees((float)RotationDegrees));
            matrix = matrix.PostConcat(SKMatrix.CreateTranslation(
                (float)(PivotX + TranslateX - offsetX),
                (float)(PivotY + TranslateY - offsetY)));

            var sampling = Math.Abs(ScaleX) >= 4 || Math.Abs(ScaleY) >= 4
                ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
                : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
            canvas.SetMatrix(matrix);
            canvas.DrawImage(SKImage.FromBitmap(srcBmp), 0, 0, sampling);
        }

        var pixels = new byte[outW * outH * 4];
        System.Runtime.InteropServices.Marshal.Copy(dstBmp.GetPixels(), pixels, 0, pixels.Length);
        return (new PixelBuffer(outW, outH, pixels), offsetX, offsetY);
    }

    public void Dispose()
    {
        if (_disposed) return;
        OriginalPixels.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

public sealed class ScaleInteraction
{
    private readonly FreeTransformSession _session;
    private readonly TransformHandle _handle;
    private readonly bool _fromCenter;
    private readonly double _startScaleX, _startScaleY;
    private readonly double _startTX, _startTY;
    private readonly double _startDistX, _startDistY;
    private readonly double _signX, _signY;
    private readonly bool _lockX, _lockY;

    public ScaleInteraction(FreeTransformSession session, TransformHandle handle, double docX, double docY, bool fromCenter)
    {
        _session = session;
        _handle = handle;
        _fromCenter = fromCenter;
        _startScaleX = session.ScaleX;
        _startScaleY = session.ScaleY;
        _startTX = session.TranslateX;
        _startTY = session.TranslateY;

        double px = session.PivotX + session.TranslateX;
        double py = session.PivotY + session.TranslateY;

        // Vector from pivot to pointer in un-rotated space
        double dx = docX - px;
        double dy = docY - py;
        double cos = Math.Cos(-session.RotationRadians);
        double sin = Math.Sin(-session.RotationRadians);
        _startDistX = dx * cos - dy * sin;
        _startDistY = dx * sin + dy * cos;

        _lockX = handle is TransformHandle.N or TransformHandle.S;
        _lockY = handle is TransformHandle.E or TransformHandle.W;

        _signX = Math.Sign(_startDistX);
        if (_signX == 0) _signX = 1;
        _signY = Math.Sign(_startDistY);
        if (_signY == 0) _signY = 1;
    }

    public void Update(double docX, double docY, bool constrainProportions)
    {
        double px = _session.PivotX + _startTX;
        double py = _session.PivotY + _startTY;
        // When not from-center, pivot for opposite-side scaling is the opposite edge — simplified: scale about center then compensate translate.
        double dx = docX - (_session.PivotX + _session.TranslateX);
        double dy = docY - (_session.PivotY + _session.TranslateY);
        // Use fixed pivot at interaction start for stable math
        dx = docX - px;
        dy = docY - py;

        double cos = Math.Cos(-_session.RotationRadians);
        double sin = Math.Sin(-_session.RotationRadians);
        double lx = dx * cos - dy * sin;
        double ly = dx * sin + dy * cos;

        double sx = _startScaleX;
        double sy = _startScaleY;

        if (!_lockX && Math.Abs(_startDistX) > 1e-6)
            sx = _startScaleX * (lx / _startDistX);
        if (!_lockY && Math.Abs(_startDistY) > 1e-6)
            sy = _startScaleY * (ly / _startDistY);

        if (_lockX) sx = _startScaleX;
        if (_lockY) sy = _startScaleY;

        // Photoshop classic: Shift locks aspect ratio.
        if (constrainProportions)
        {
            double fx = Math.Abs(_startDistX) < 1e-6 ? 1 : lx / _startDistX;
            double fy = Math.Abs(_startDistY) < 1e-6 ? 1 : ly / _startDistY;
            double f;
            if (_lockX) f = fy;
            else if (_lockY) f = fx;
            else f = Math.Abs(fx) >= Math.Abs(fy) ? fx : fy;
            sx = _startScaleX * f;
            sy = _startScaleY * f;
        }

        // Clamp extreme scales
        sx = Math.Clamp(sx, -100, 100);
        sy = Math.Clamp(sy, -100, 100);
        if (Math.Abs(sx) < 0.001) sx = 0.001 * Math.Sign(sx == 0 ? 1 : sx);
        if (Math.Abs(sy) < 0.001) sy = 0.001 * Math.Sign(sy == 0 ? 1 : sy);

        _session.ScaleX = sx;
        _session.ScaleY = sy;

        if (!_fromCenter)
        {
            // Keep the opposite side fixed: shift translation so opposite handle stays put.
            // Approximate by compensating half the size delta along handle axis in rotated space.
            double dScaleX = sx - _startScaleX;
            double dScaleY = sy - _startScaleY;
            double hw = _session.BaseWidth / 2.0;
            double hh = _session.BaseHeight / 2.0;

            double localShiftX = 0, localShiftY = 0;
            switch (_handle)
            {
                case TransformHandle.E: localShiftX = dScaleX * hw / 2; break;
                case TransformHandle.W: localShiftX = -dScaleX * hw / 2; break;
                case TransformHandle.S: localShiftY = dScaleY * hh / 2; break;
                case TransformHandle.N: localShiftY = -dScaleY * hh / 2; break;
                case TransformHandle.SE: localShiftX = dScaleX * hw / 2; localShiftY = dScaleY * hh / 2; break;
                case TransformHandle.NE: localShiftX = dScaleX * hw / 2; localShiftY = -dScaleY * hh / 2; break;
                case TransformHandle.SW: localShiftX = -dScaleX * hw / 2; localShiftY = dScaleY * hh / 2; break;
                case TransformHandle.NW: localShiftX = -dScaleX * hw / 2; localShiftY = -dScaleY * hh / 2; break;
            }

            double rcos = Math.Cos(_session.RotationRadians);
            double rsin = Math.Sin(_session.RotationRadians);
            _session.TranslateX = _startTX + localShiftX * rcos - localShiftY * rsin;
            _session.TranslateY = _startTY + localShiftX * rsin + localShiftY * rcos;
        }
        else
        {
            _session.TranslateX = _startTX;
            _session.TranslateY = _startTY;
        }
    }
}

public sealed class RotateInteraction
{
    private readonly FreeTransformSession _session;
    private readonly double _startAngle;
    private readonly double _startRotation;

    public RotateInteraction(FreeTransformSession session, double docX, double docY)
    {
        _session = session;
        _startRotation = session.RotationDegrees;
        double px = session.PivotX + session.TranslateX;
        double py = session.PivotY + session.TranslateY;
        _startAngle = Math.Atan2(docY - py, docX - px);
    }

    public void Update(double docX, double docY, bool snap15)
    {
        double px = _session.PivotX + _session.TranslateX;
        double py = _session.PivotY + _session.TranslateY;
        double angle = Math.Atan2(docY - py, docX - px);
        double deltaDeg = (angle - _startAngle) * 180.0 / Math.PI;
        double rot = _startRotation + deltaDeg;
        if (snap15)
            rot = Math.Round(rot / 15.0) * 15.0;
        _session.RotationDegrees = rot;
    }
}

public sealed class MoveInteraction
{
    private readonly FreeTransformSession _session;
    private readonly double _startDocX, _startDocY;
    private readonly double _startTX, _startTY;

    public MoveInteraction(FreeTransformSession session, double docX, double docY)
    {
        _session = session;
        _startDocX = docX;
        _startDocY = docY;
        _startTX = session.TranslateX;
        _startTY = session.TranslateY;
    }

    public void Update(double docX, double docY, bool constrainAxis)
    {
        double dx = docX - _startDocX;
        double dy = docY - _startDocY;
        if (constrainAxis)
        {
            if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0;
            else dx = 0;
        }
        _session.TranslateX = _startTX + dx;
        _session.TranslateY = _startTY + dy;
    }
}
