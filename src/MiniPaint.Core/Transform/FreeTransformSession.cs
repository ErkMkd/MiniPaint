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
    private PixelBuffer? _maskSource;

    public Layer Layer { get; }
    public PixelBuffer OriginalPixels { get; }

    /// <summary>The layer mask at the start (null when the layer has none); it follows the transform.</summary>
    public byte[]? OriginalMask { get; }
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
        OriginalMask = (byte[]?)layer.Mask?.Clone();
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
    /// Photoshop: default anchor = opposite handle; Alt = layer center.
    /// </summary>
    public ScaleInteraction BeginScale(TransformHandle handle) =>
        new(this, handle);

    public RotateInteraction BeginRotate(double docX, double docY) =>
        new(this, docX, docY);

    public MoveInteraction BeginMove(double docX, double docY) =>
        new(this, docX, docY);

    /// <summary>
    /// Bake transform into a new pixel buffer + document offset (AABB of result).
    /// </summary>
    public (PixelBuffer pixels, int offsetX, int offsetY) Bake() => Bake(OriginalPixels);

    /// <summary>The original layer mask with the current transform (same size as <see cref="Bake()"/>), or null.</summary>
    public byte[]? BakeMask()
    {
        if (OriginalMask is null) return null;
        _maskSource ??= LayerMask.ToBuffer(OriginalMask, BaseWidth, BaseHeight);
        var (pixels, _, _) = Bake(_maskSource);
        using (pixels)
            return LayerMask.FromAlpha(pixels);
    }

    /// <summary>
    /// Bake another buffer the same size as the original pixels with the same transform
    /// (e.g. the selection mask, so it follows the lifted pixels exactly).
    /// </summary>
    public (PixelBuffer pixels, int offsetX, int offsetY) Bake(PixelBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Width != BaseWidth || source.Height != BaseHeight)
            throw new ArgumentException("Source must match the transformed layer size.", nameof(source));

        var corners = GetCorners();
        double minX = corners.Min(c => c.x);
        double minY = corners.Min(c => c.y);
        double maxX = corners.Max(c => c.x);
        double maxY = corners.Max(c => c.y);

        int outW = Math.Max(1, (int)Math.Ceiling(maxX - minX) + 1);
        int outH = Math.Max(1, (int)Math.Ceiling(maxY - minY) + 1);
        int offsetX = (int)Math.Floor(minX);
        int offsetY = (int)Math.Floor(minY);

        using var srcBmp = ImageCodec.ToSkBitmap(source);
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
        _maskSource?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

public sealed class ScaleInteraction
{
    private readonly FreeTransformSession _session;
    private readonly TransformHandle _handle;
    private readonly double _startScaleX, _startScaleY;
    private readonly double _startTX, _startTY;
    private readonly double _hw, _hh;
    private readonly double _handleLocalX, _handleLocalY;
    private readonly double _oppLocalX, _oppLocalY;
    private readonly bool _lockX, _lockY;
    private readonly double _rotCos, _rotSin, _invCos, _invSin;

    public ScaleInteraction(FreeTransformSession session, TransformHandle handle)
    {
        _session = session;
        _handle = handle;
        _startScaleX = session.ScaleX;
        _startScaleY = session.ScaleY;
        _startTX = session.TranslateX;
        _startTY = session.TranslateY;
        _hw = session.BaseWidth / 2.0;
        _hh = session.BaseHeight / 2.0;

        (_handleLocalX, _handleLocalY) = HandleLocal(handle, _hw, _hh);
        (_oppLocalX, _oppLocalY) = OppositeLocal(handle, _hw, _hh);

        _lockX = handle is TransformHandle.N or TransformHandle.S;
        _lockY = handle is TransformHandle.E or TransformHandle.W;

        _rotCos = Math.Cos(session.RotationRadians);
        _rotSin = Math.Sin(session.RotationRadians);
        _invCos = Math.Cos(-session.RotationRadians);
        _invSin = Math.Sin(-session.RotationRadians);
    }

    /// <param name="fromCenter">Alt: anchor at layer center; otherwise opposite handle (Photoshop default).</param>
    public void Update(double docX, double docY, bool constrainProportions, bool fromCenter)
    {
        // Anchor in unscaled local coords (relative to layer center).
        double ax = fromCenter ? 0 : _oppLocalX;
        double ay = fromCenter ? 0 : _oppLocalY;

        // Document-space center and fixed anchor at drag start (using start scales/translate).
        double centerX0 = _session.PivotX + _startTX;
        double centerY0 = _session.PivotY + _startTY;
        var (anchorDocX, anchorDocY) = LocalToDoc(centerX0, centerY0, ax * _startScaleX, ay * _startScaleY);

        // Mouse relative to fixed anchor, in un-rotated local axes.
        double vx = docX - anchorDocX;
        double vy = docY - anchorDocY;
        double vLocalX = vx * _invCos - vy * _invSin;
        double vLocalY = vx * _invSin + vy * _invCos;

        // Unscaled local delta from anchor → moving handle.
        double spanX = _handleLocalX - ax;
        double spanY = _handleLocalY - ay;

        double sx = _startScaleX;
        double sy = _startScaleY;

        if (!_lockX && Math.Abs(spanX) > 1e-9)
            sx = vLocalX / spanX;
        if (!_lockY && Math.Abs(spanY) > 1e-9)
            sy = vLocalY / spanY;

        if (_lockX) sx = _startScaleX;
        if (_lockY) sy = _startScaleY;

        // Shift: lock aspect ratio of scales (Photoshop classic).
        if (constrainProportions)
        {
            double absX = Math.Abs(sx);
            double absY = Math.Abs(sy);
            double startRatio = Math.Abs(_startScaleY) < 1e-9
                ? 1
                : Math.Abs(_startScaleX / _startScaleY);

            if (_lockX)
            {
                absX = absY * startRatio;
            }
            else if (_lockY)
            {
                absY = absX / startRatio;
            }
            else
            {
                // Pick dominant axis change vs start, apply uniform factor to both start scales.
                double fx = Math.Abs(_startScaleX) < 1e-9 ? absX : absX / Math.Abs(_startScaleX);
                double fy = Math.Abs(_startScaleY) < 1e-9 ? absY : absY / Math.Abs(_startScaleY);
                double f = fx >= fy ? fx : fy;
                absX = Math.Abs(_startScaleX) * f;
                absY = Math.Abs(_startScaleY) * f;
            }

            sx = CopySign(absX, sx);
            sy = CopySign(absY, sy);
        }

        sx = ClampScale(sx);
        sy = ClampScale(sy);

        _session.ScaleX = sx;
        _session.ScaleY = sy;

        // Keep the anchor fixed in document space:
        // Center1 + R*(sx*ax, sy*ay) = anchorDoc
        double scaledAx = ax * sx;
        double scaledAy = ay * sy;
        double rotAx = scaledAx * _rotCos - scaledAy * _rotSin;
        double rotAy = scaledAx * _rotSin + scaledAy * _rotCos;
        double centerX1 = anchorDocX - rotAx;
        double centerY1 = anchorDocY - rotAy;
        _session.TranslateX = centerX1 - _session.PivotX;
        _session.TranslateY = centerY1 - _session.PivotY;
    }

    private (double x, double y) LocalToDoc(double centerX, double centerY, double localX, double localY)
    {
        double rx = localX * _rotCos - localY * _rotSin;
        double ry = localX * _rotSin + localY * _rotCos;
        return (centerX + rx, centerY + ry);
    }

    private static double ClampScale(double s)
    {
        s = Math.Clamp(s, -100, 100);
        if (Math.Abs(s) < 0.001)
            s = 0.001 * (s < 0 ? -1 : 1);
        return s;
    }

    private static double CopySign(double magnitude, double signSource) =>
        Math.Abs(magnitude) * (signSource < 0 ? -1 : 1);

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

    /// <summary>Photoshop default: opposite edge/corner stays put.</summary>
    private static (double x, double y) OppositeLocal(TransformHandle handle, double hw, double hh) => handle switch
    {
        TransformHandle.NW => (hw, hh),   // SE
        TransformHandle.N => (0, hh),     // S
        TransformHandle.NE => (-hw, hh),  // SW
        TransformHandle.E => (-hw, 0),    // W
        TransformHandle.SE => (-hw, -hh), // NW
        TransformHandle.S => (0, -hh),    // N
        TransformHandle.SW => (hw, -hh),  // NE
        TransformHandle.W => (hw, 0),     // E
        _ => (0, 0)
    };
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
