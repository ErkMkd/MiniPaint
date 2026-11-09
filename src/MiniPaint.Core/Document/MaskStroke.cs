using MiniPaint.Core.History;

namespace MiniPaint.Core.Document;

/// <summary>
/// Round mask brush: diameter in document pixels, hardness 0 (soft edge across the whole radius)
/// to 1 (hard edge, anti-aliased), opacity 0–1, and what it paints: white reveals the layer,
/// black hides it.
/// </summary>
public readonly record struct MaskBrushOptions(double Size, double Hardness, double Opacity, bool Reveal)
{
    public const double MinSize = 1;
    public const double MaxSize = 2000;
}

/// <summary>
/// One mask brush stroke on a layer: round dabs along the pointer path. As in Photoshop, a stroke
/// never goes past the brush opacity however often it passes over a pixel; the next stroke does.
/// A layer without a mask gets one (showing everything) when the stroke starts.
/// </summary>
public sealed class MaskStroke
{
    /// <summary>Dabs every 10% of the diameter: dense enough that the edge does not scallop.</summary>
    private const double SpacingRatio = 0.1;

    private readonly Layer _layer;
    private readonly MaskBrushOptions _options;
    private readonly byte[]? _before;
    private readonly byte[] _start;
    private readonly byte[] _coverage;
    private readonly int _width;
    private readonly int _height;
    private bool _hasLast;
    private double _lastX, _lastY;
    private double _sinceDab;
    private bool _painted;

    public MaskStroke(Layer layer, MaskBrushOptions options)
    {
        _layer = layer ?? throw new ArgumentNullException(nameof(layer));
        _options = options with
        {
            Size = Math.Clamp(options.Size, MaskBrushOptions.MinSize, MaskBrushOptions.MaxSize),
            Hardness = Math.Clamp(options.Hardness, 0, 1),
            Opacity = Math.Clamp(options.Opacity, 0, 1),
        };
        _width = layer.Pixels.Width;
        _height = layer.Pixels.Height;
        _before = (byte[]?)layer.Mask?.Clone();
        if (layer.Mask is null)
            layer.SetMask(LayerMask.Filled(_width, _height, LayerMask.Reveal));
        _start = (byte[])layer.Mask!.Clone();
        _coverage = new byte[_width * _height];
    }

    public Layer Layer => _layer;

    /// <summary>
    /// Paint to document point x, y: one dab for the first point, then dabs along the segment from
    /// the previous point. True when a mask value changed.
    /// </summary>
    public bool LineTo(double docX, double docY)
    {
        double x = docX - _layer.OffsetX;
        double y = docY - _layer.OffsetY;
        bool changed = false;
        if (!_hasLast)
        {
            changed = Dab(x, y);
            _hasLast = true;
        }
        else
        {
            double dx = x - _lastX;
            double dy = y - _lastY;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double spacing = Math.Max(1, _options.Size * SpacingRatio);
            double t = spacing - _sinceDab;
            double lastDab = -1;
            for (; t <= length; t += spacing)
            {
                changed |= Dab(_lastX + dx * t / length, _lastY + dy * t / length);
                lastDab = t;
            }
            _sinceDab = lastDab < 0 ? _sinceDab + length : length - lastDab;
        }
        _lastX = x;
        _lastY = y;
        _painted |= changed;
        return changed;
    }

    /// <summary>
    /// The stroke as one undo step, or null when it painted nothing (a mask added for it is removed).
    /// </summary>
    public IEditCommand? Finish(Action? onChanged = null)
    {
        if (!_painted)
        {
            _layer.SetMask(_before);
            return null;
        }
        string name = _before is null ? "Add Layer Mask" : "Mask Brush";
        return new LayerMaskCommand(name, _layer, _before, (byte[])_layer.Mask!.Clone(), onChanged);
    }

    /// <summary>One round dab centered at layer-local x, y (pixel centers at +0.5).</summary>
    private bool Dab(double x, double y)
    {
        var mask = _layer.Mask;
        if (mask is null) return false;

        double radius = _options.Size / 2;
        double soft = radius * (1 - _options.Hardness);
        bool hardEdge = soft < 1;
        double inner = radius - soft;
        double reach = hardEdge ? radius + 0.5 : radius;
        int x0 = Math.Max(0, (int)Math.Floor(x - reach));
        int x1 = Math.Min(_width - 1, (int)Math.Ceiling(x + reach));
        int y0 = Math.Max(0, (int)Math.Floor(y - reach));
        int y1 = Math.Min(_height - 1, (int)Math.Ceiling(y + reach));
        int target = _options.Reveal ? LayerMask.Reveal : LayerMask.Hide;
        bool changed = false;

        for (int py = y0; py <= y1; py++)
        {
            double dy = py + 0.5 - y;
            for (int px = x0; px <= x1; px++)
            {
                double dx = px + 0.5 - x;
                double d = Math.Sqrt(dx * dx + dy * dy);
                double a;
                if (hardEdge)
                    a = Math.Clamp(radius + 0.5 - d, 0, 1); // 1-pixel anti-aliased edge
                else if (d <= inner)
                    a = 1;
                else if (d >= radius)
                    continue;
                else
                {
                    double t = (d - inner) / soft;
                    a = 1 - t * t * (3 - 2 * t); // smoothstep falloff
                }

                byte cover = (byte)(a * 255 + 0.5);
                int i = py * _width + px;
                if (cover <= _coverage[i]) continue;
                _coverage[i] = cover;
                int start = _start[i];
                byte value = (byte)Math.Round(start + (target - start) * (cover / 255.0) * _options.Opacity);
                if (value != mask[i])
                {
                    mask[i] = value;
                    changed = true;
                }
            }
        }
        return changed;
    }
}
