using MiniPaint.Core.Composition;
using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.History;

public sealed class LayerPixelsCommand : IEditCommand
{
    private readonly Layer _layer;
    private readonly PixelBuffer _before;
    private readonly PixelBuffer _after;
    private readonly Action? _onChanged;

    public string Name { get; }

    public LayerPixelsCommand(string name, Layer layer, PixelBuffer before, PixelBuffer after, Action? onChanged = null)
    {
        Name = name;
        _layer = layer;
        _before = before;
        _after = after;
        _onChanged = onChanged;
    }

    public void Execute()
    {
        _layer.ReplacePixels(_after.Clone());
        _onChanged?.Invoke();
    }

    public void Undo()
    {
        _layer.ReplacePixels(_before.Clone());
        _onChanged?.Invoke();
    }
}

public sealed class DeleteLayerCommand : IEditCommand
{
    private readonly PaintDocument _document;
    private Guid _layerId;
    private PixelBuffer? _pixels;
    private string _name = "";
    private bool _visible = true;
    private float _opacity = 1f;
    private BlendMode _blendMode = BlendMode.Normal;
    private int _ox, _oy, _index;
    private readonly Action? _onChanged;

    public string Name => "Delete Layer";

    public DeleteLayerCommand(PaintDocument document, Guid layerId, Action? onChanged = null)
    {
        _document = document;
        _layerId = layerId;
        _onChanged = onChanged;
    }

    public void Execute()
    {
        var idx = -1;
        Layer? layer = null;
        for (int i = 0; i < _document.Layers.Count; i++)
        {
            if (_document.Layers[i].Id == _layerId)
            {
                idx = i;
                layer = _document.Layers[i];
                break;
            }
        }
        if (layer is null || idx < 0) return;

        _index = idx;
        _name = layer.Name;
        _visible = layer.IsVisible;
        _opacity = layer.Opacity;
        _blendMode = layer.BlendMode;
        _ox = layer.OffsetX;
        _oy = layer.OffsetY;
        _pixels?.Dispose();
        _pixels = layer.Pixels.Clone();
        _document.RemoveLayer(_layerId);
        _onChanged?.Invoke();
    }

    public void Undo()
    {
        if (_pixels is null) return;
        var layer = new Layer(_name, _pixels.Clone(), _ox, _oy)
        {
            IsVisible = _visible,
            Opacity = _opacity,
            BlendMode = _blendMode
        };
        _document.AddLayer(layer, _index);
        _layerId = layer.Id;
        _onChanged?.Invoke();
    }
}

public sealed class ReorderLayerCommand : IEditCommand
{
    private readonly PaintDocument _document;
    private readonly Guid _layerId;
    private readonly int _from;
    private readonly int _to;
    private readonly Action? _onChanged;

    public string Name => "Reorder Layer";

    public ReorderLayerCommand(PaintDocument document, Guid layerId, int from, int to, Action? onChanged = null)
    {
        _document = document;
        _layerId = layerId;
        _from = from;
        _to = to;
        _onChanged = onChanged;
    }

    public void Execute()
    {
        _document.MoveLayer(_layerId, _to);
        _onChanged?.Invoke();
    }

    public void Undo()
    {
        _document.MoveLayer(_layerId, _from);
        _onChanged?.Invoke();
    }
}

public sealed class LayerPropertyCommand : IEditCommand
{
    private readonly Layer _layer;
    private readonly string _nameBefore;
    private readonly string _nameAfter;
    private readonly bool _visBefore;
    private readonly bool _visAfter;
    private readonly float _opBefore;
    private readonly float _opAfter;
    private readonly BlendMode _blendBefore;
    private readonly BlendMode _blendAfter;
    private readonly int _oxBefore, _oyBefore, _oxAfter, _oyAfter;
    private readonly Action? _onChanged;

    public string Name { get; }

    public LayerPropertyCommand(
        string commandName,
        Layer layer,
        string nameBefore, string nameAfter,
        bool visBefore, bool visAfter,
        float opBefore, float opAfter,
        BlendMode blendBefore, BlendMode blendAfter,
        int oxBefore, int oyBefore, int oxAfter, int oyAfter,
        Action? onChanged = null)
    {
        Name = commandName;
        _layer = layer;
        _nameBefore = nameBefore;
        _nameAfter = nameAfter;
        _visBefore = visBefore;
        _visAfter = visAfter;
        _opBefore = opBefore;
        _opAfter = opAfter;
        _blendBefore = blendBefore;
        _blendAfter = blendAfter;
        _oxBefore = oxBefore;
        _oyBefore = oyBefore;
        _oxAfter = oxAfter;
        _oyAfter = oyAfter;
        _onChanged = onChanged;
    }

    public static LayerPropertyCommand Rename(Layer layer, string newName, Action? onChanged = null) =>
        new("Rename Layer", layer, layer.Name, newName, layer.IsVisible, layer.IsVisible,
            layer.Opacity, layer.Opacity, layer.BlendMode, layer.BlendMode,
            layer.OffsetX, layer.OffsetY, layer.OffsetX, layer.OffsetY, onChanged);

    public static LayerPropertyCommand Visibility(Layer layer, bool visible, Action? onChanged = null) =>
        new("Layer Visibility", layer, layer.Name, layer.Name, layer.IsVisible, visible,
            layer.Opacity, layer.Opacity, layer.BlendMode, layer.BlendMode,
            layer.OffsetX, layer.OffsetY, layer.OffsetX, layer.OffsetY, onChanged);

    public static LayerPropertyCommand Opacity(Layer layer, float opacity, Action? onChanged = null) =>
        Opacity(layer, layer.Opacity, opacity, onChanged);

    public static LayerPropertyCommand Opacity(Layer layer, float from, float to, Action? onChanged = null) =>
        new("Layer Opacity", layer, layer.Name, layer.Name, layer.IsVisible, layer.IsVisible,
            from, to, layer.BlendMode, layer.BlendMode,
            layer.OffsetX, layer.OffsetY, layer.OffsetX, layer.OffsetY, onChanged);

    public static LayerPropertyCommand BlendModeChange(Layer layer, BlendMode blendMode, Action? onChanged = null) =>
        new("Layer Blend Mode", layer, layer.Name, layer.Name, layer.IsVisible, layer.IsVisible,
            layer.Opacity, layer.Opacity, layer.BlendMode, blendMode,
            layer.OffsetX, layer.OffsetY, layer.OffsetX, layer.OffsetY, onChanged);

    public static LayerPropertyCommand Move(Layer layer, int x, int y, Action? onChanged = null) =>
        new("Move Layer", layer, layer.Name, layer.Name, layer.IsVisible, layer.IsVisible,
            layer.Opacity, layer.Opacity, layer.BlendMode, layer.BlendMode,
            layer.OffsetX, layer.OffsetY, x, y, onChanged);

    public void Execute() => Apply(_nameAfter, _visAfter, _opAfter, _blendAfter, _oxAfter, _oyAfter);
    public void Undo() => Apply(_nameBefore, _visBefore, _opBefore, _blendBefore, _oxBefore, _oyBefore);

    private void Apply(string name, bool vis, float op, BlendMode blend, int ox, int oy)
    {
        _layer.Name = name;
        _layer.IsVisible = vis;
        _layer.Opacity = op;
        _layer.BlendMode = blend;
        _layer.OffsetX = ox;
        _layer.OffsetY = oy;
        _onChanged?.Invoke();
    }
}

/// <summary>
/// Paste creates a brand-new layer each Execute from stored pixel snapshot (safe across undo/redo dispose).
/// </summary>
public sealed class PasteLayerCommand : IEditCommand
{
    private readonly PaintDocument _document;
    private readonly string _layerName;
    private readonly PixelBuffer _pixels;
    private readonly int _ox;
    private readonly int _oy;
    private Guid? _currentLayerId;
    private readonly Action? _onChanged;

    public string Name => "Paste Layer";

    public PasteLayerCommand(PaintDocument document, Layer layerTemplate, Action? onChanged = null)
    {
        _document = document;
        _layerName = layerTemplate.Name;
        _pixels = layerTemplate.Pixels.Clone();
        _ox = layerTemplate.OffsetX;
        _oy = layerTemplate.OffsetY;
        _onChanged = onChanged;
        // First execute will add; if template was already added by caller, pass via Record after add.
        _currentLayerId = layerTemplate.Id;
    }

    /// <summary>Use when layer is not yet in the document.</summary>
    public static PasteLayerCommand CreatePending(PaintDocument document, string name, PixelBuffer pixels, int x, int y, Action? onChanged = null)
    {
        var temp = new Layer(name, pixels, x, y);
        var cmd = new PasteLayerCommand(document, temp, onChanged);
        cmd._currentLayerId = null;
        temp.Dispose();
        return cmd;
    }

    public Layer? LastLayer { get; private set; }

    public void Execute()
    {
        if (_currentLayerId is Guid existing)
        {
            // Already present from Record path — ensure reference.
            try
            {
                LastLayer = _document.RequireLayer(existing);
                _onChanged?.Invoke();
                return;
            }
            catch (ArgumentException)
            {
                _currentLayerId = null;
            }
        }

        var layer = new Layer(_layerName, _pixels.Clone(), _ox, _oy);
        _document.AddLayer(layer);
        _currentLayerId = layer.Id;
        LastLayer = layer;
        _onChanged?.Invoke();
    }

    public void Undo()
    {
        if (_currentLayerId is Guid id)
        {
            try { _document.RemoveLayer(id); } catch { /* already gone */ }
            _currentLayerId = null;
            LastLayer = null;
            _onChanged?.Invoke();
        }
    }
}

public sealed class TransformLayerCommand : IEditCommand
{
    private readonly Layer _layer;
    private readonly PixelBuffer _before;
    private readonly PixelBuffer _after;
    private readonly int _beforeOx, _beforeOy, _afterOx, _afterOy;
    private readonly Action? _onChanged;

    public string Name => "Free Transform";

    public TransformLayerCommand(
        Layer layer,
        PixelBuffer before, int beforeOx, int beforeOy,
        PixelBuffer after, int afterOx, int afterOy,
        Action? onChanged = null)
    {
        _layer = layer;
        _before = before;
        _after = after;
        _beforeOx = beforeOx;
        _beforeOy = beforeOy;
        _afterOx = afterOx;
        _afterOy = afterOy;
        _onChanged = onChanged;
    }

    public void Execute()
    {
        _layer.ReplacePixels(_after.Clone());
        _layer.OffsetX = _afterOx;
        _layer.OffsetY = _afterOy;
        _onChanged?.Invoke();
    }

    public void Undo()
    {
        _layer.ReplacePixels(_before.Clone());
        _layer.OffsetX = _beforeOx;
        _layer.OffsetY = _beforeOy;
        _onChanged?.Invoke();
    }
}

public sealed class AddEmptyLayerCommand : IEditCommand
{
    private readonly PaintDocument _document;
    private readonly string _name;
    private Guid? _layerId;
    private readonly Action? _onChanged;

    public string Name => "Add Layer";
    public Layer? LastLayer { get; private set; }

    public AddEmptyLayerCommand(PaintDocument document, string name, Action? onChanged = null)
    {
        _document = document;
        _name = name;
        _onChanged = onChanged;
    }

    public void Execute()
    {
        if (_layerId is Guid id)
        {
            try
            {
                LastLayer = _document.RequireLayer(id);
                return;
            }
            catch (ArgumentException) { _layerId = null; }
        }

        var layer = _document.AddEmptyLayer(_name);
        _layerId = layer.Id;
        LastLayer = layer;
        _onChanged?.Invoke();
    }

    public void Undo()
    {
        if (_layerId is Guid id)
        {
            try { _document.RemoveLayer(id); } catch { }
            _layerId = null;
            LastLayer = null;
            _onChanged?.Invoke();
        }
    }
}

/// <summary>
/// Undoable document canvas/image resize. Restores dimensions + per-layer pixels and offsets.
/// </summary>
public sealed class DocumentResizeCommand : IEditCommand, IDisposable
{
    private readonly PaintDocument _document;
    private readonly DocumentSnapshot _before;
    private readonly DocumentSnapshot _after;
    private readonly Action? _onChanged;
    private bool _disposed;

    public string Name { get; }

    private DocumentResizeCommand(
        string name,
        PaintDocument document,
        DocumentSnapshot before,
        DocumentSnapshot after,
        Action? onChanged)
    {
        Name = name;
        _document = document;
        _before = before;
        _after = after;
        _onChanged = onChanged;
    }

    public static DocumentResizeCommand CanvasSize(
        PaintDocument document,
        int newWidth,
        int newHeight,
        CanvasAnchor anchor,
        Action? onChanged = null)
    {
        var before = DocumentSnapshot.Capture(document);
        DocumentResize.ResizeCanvas(document, newWidth, newHeight, anchor);
        var after = DocumentSnapshot.Capture(document);
        return new DocumentResizeCommand("Canvas Size", document, before, after, onChanged);
    }

    public static DocumentResizeCommand ImageSize(
        PaintDocument document,
        int newWidth,
        int newHeight,
        Action? onChanged = null)
    {
        var before = DocumentSnapshot.Capture(document);
        DocumentResize.ResizeImage(document, newWidth, newHeight);
        var after = DocumentSnapshot.Capture(document);
        return new DocumentResizeCommand("Image Size", document, before, after, onChanged);
    }

    public void Execute() => Apply(_after);

    public void Undo() => Apply(_before);

    private void Apply(DocumentSnapshot snap)
    {
        snap.RestoreInto(_document);
        _onChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _before.Dispose();
        _after.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

internal sealed class DocumentSnapshot : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    private readonly LayerSnap[] _layers;
    private bool _disposed;

    private DocumentSnapshot(int width, int height, LayerSnap[] layers)
    {
        Width = width;
        Height = height;
        _layers = layers;
    }

    public static DocumentSnapshot Capture(PaintDocument document)
    {
        var layers = new LayerSnap[document.Layers.Count];
        for (int i = 0; i < document.Layers.Count; i++)
        {
            var layer = document.Layers[i];
            layers[i] = new LayerSnap(
                layer.Id,
                layer.Pixels.Clone(),
                layer.OffsetX,
                layer.OffsetY);
        }

        return new DocumentSnapshot(document.Width, document.Height, layers);
    }

    public void RestoreInto(PaintDocument document)
    {
        document.SetSize(Width, Height);
        foreach (var snap in _layers)
        {
            Layer? layer = null;
            try { layer = document.RequireLayer(snap.Id); }
            catch (ArgumentException) { continue; }

            layer.ReplacePixels(snap.Pixels.Clone());
            layer.OffsetX = snap.OffsetX;
            layer.OffsetY = snap.OffsetY;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var snap in _layers)
            snap.Pixels.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed class LayerSnap(Guid id, PixelBuffer pixels, int offsetX, int offsetY)
    {
        public Guid Id { get; } = id;
        public PixelBuffer Pixels { get; } = pixels;
        public int OffsetX { get; } = offsetX;
        public int OffsetY { get; } = offsetY;
    }
}
