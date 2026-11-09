using MiniPaint.Core.ColorMode;
using MiniPaint.Core.Composition;
using MiniPaint.Core.Document;
using MiniPaint.Core.Filters;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Selection;

namespace MiniPaint.Core.History;

public sealed class ColorSettingsCommand : IEditCommand
{
    private readonly PaintDocument _document;
    private readonly DocumentColorSettings _before;
    private readonly DocumentColorSettings _after;
    private readonly Action? _onChanged;

    public string Name => "Image Mode";

    public ColorSettingsCommand(
        PaintDocument document,
        DocumentColorSettings before,
        DocumentColorSettings after,
        Action? onChanged = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _before = before.Clone();
        _after = after.Clone();
        _after.Sanitize();
        _onChanged = onChanged;
    }

    public void Execute()
    {
        _document.ColorSettings.CopyFrom(_after);
        _document.IsDirty = true;
        _onChanged?.Invoke();
    }

    public void Undo()
    {
        _document.ColorSettings.CopyFrom(_before);
        _document.IsDirty = true;
        _onChanged?.Invoke();
    }
}

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

/// <summary>Selection change (marquee, Select All, Deselect, Inverse, Transform Selection…).</summary>
public sealed class SelectionCommand : IEditCommand
{
    private readonly SelectionState _selection;
    private readonly SelectionSnapshot _before;
    private readonly SelectionSnapshot _after;
    private readonly Action? _onChanged;

    public string Name { get; }

    public SelectionCommand(
        string name,
        SelectionState selection,
        SelectionSnapshot before,
        SelectionSnapshot after,
        Action? onChanged = null)
    {
        Name = name;
        _selection = selection;
        _before = before;
        _after = after;
        _onChanged = onChanged;
    }

    public void Execute()
    {
        _selection.Restore(_after);
        _onChanged?.Invoke();
    }

    public void Undo()
    {
        _selection.Restore(_before);
        _onChanged?.Invoke();
    }
}

/// <summary>Several commands as one undo step (e.g. moved pixels + moved marquee); undone in reverse.</summary>
public sealed class CompoundCommand : IEditCommand
{
    private readonly IEditCommand[] _commands;

    public string Name { get; }

    public CompoundCommand(string name, params IEditCommand[] commands)
    {
        Name = name;
        _commands = commands;
    }

    public void Execute()
    {
        foreach (var command in _commands)
            command.Execute();
    }

    public void Undo()
    {
        for (int i = _commands.Length - 1; i >= 0; i--)
            _commands[i].Undo();
    }
}

public sealed class LayerPropertyCommand : IEditCommand
{
    private readonly LayerNode _layer;
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
        LayerNode layer,
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

    public static LayerPropertyCommand Rename(LayerNode layer, string newName, Action? onChanged = null) =>
        new("Rename Layer", layer, layer.Name, newName, layer.IsVisible, layer.IsVisible,
            layer.Opacity, layer.Opacity, layer.BlendMode, layer.BlendMode,
            OffsetX(layer), OffsetY(layer), OffsetX(layer), OffsetY(layer), onChanged);

    public static LayerPropertyCommand Visibility(LayerNode layer, bool visible, Action? onChanged = null) =>
        new("Layer Visibility", layer, layer.Name, layer.Name, layer.IsVisible, visible,
            layer.Opacity, layer.Opacity, layer.BlendMode, layer.BlendMode,
            OffsetX(layer), OffsetY(layer), OffsetX(layer), OffsetY(layer), onChanged);

    public static LayerPropertyCommand Opacity(LayerNode layer, float opacity, Action? onChanged = null) =>
        Opacity(layer, layer.Opacity, opacity, onChanged);

    public static LayerPropertyCommand Opacity(LayerNode layer, float from, float to, Action? onChanged = null) =>
        new("Layer Opacity", layer, layer.Name, layer.Name, layer.IsVisible, layer.IsVisible,
            from, to, layer.BlendMode, layer.BlendMode,
            OffsetX(layer), OffsetY(layer), OffsetX(layer), OffsetY(layer), onChanged);

    public static LayerPropertyCommand BlendModeChange(LayerNode layer, BlendMode blendMode, Action? onChanged = null) =>
        new("Layer Blend Mode", layer, layer.Name, layer.Name, layer.IsVisible, layer.IsVisible,
            layer.Opacity, layer.Opacity, layer.BlendMode, blendMode,
            OffsetX(layer), OffsetY(layer), OffsetX(layer), OffsetY(layer), onChanged);

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
        if (_layer is Layer pixels)
        {
            pixels.OffsetX = ox;
            pixels.OffsetY = oy;
        }
        _onChanged?.Invoke();
    }

    private static int OffsetX(LayerNode node) => node is Layer layer ? layer.OffsetX : 0;
    private static int OffsetY(LayerNode node) => node is Layer layer ? layer.OffsetY : 0;
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
            try { _document.RemoveNode(id); } catch { /* already gone */ }
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
    private readonly byte[]? _maskBefore;
    private readonly byte[]? _maskAfter;
    private readonly int _beforeOx, _beforeOy, _afterOx, _afterOy;
    private readonly Action? _onChanged;

    public string Name => "Free Transform";

    /// <summary>The masks (null without one) are transformed with the pixels.</summary>
    public TransformLayerCommand(
        Layer layer,
        PixelBuffer before, byte[]? maskBefore, int beforeOx, int beforeOy,
        PixelBuffer after, byte[]? maskAfter, int afterOx, int afterOy,
        Action? onChanged = null)
    {
        _layer = layer;
        _before = before;
        _after = after;
        _maskBefore = maskBefore;
        _maskAfter = maskAfter;
        _beforeOx = beforeOx;
        _beforeOy = beforeOy;
        _afterOx = afterOx;
        _afterOy = afterOy;
        _onChanged = onChanged;
    }

    public void Execute()
    {
        _layer.ReplacePixels(_after.Clone(), (byte[]?)_maskAfter?.Clone());
        _layer.OffsetX = _afterOx;
        _layer.OffsetY = _afterOy;
        _onChanged?.Invoke();
    }

    public void Undo()
    {
        _layer.ReplacePixels(_before.Clone(), (byte[]?)_maskBefore?.Clone());
        _layer.OffsetX = _beforeOx;
        _layer.OffsetY = _beforeOy;
        _onChanged?.Invoke();
    }
}

/// <summary>Add, paint, apply or delete a layer mask (null = no mask).</summary>
public sealed class LayerMaskCommand : IEditCommand
{
    private readonly Layer _layer;
    private readonly byte[]? _before;
    private readonly byte[]? _after;
    private readonly Action? _onChanged;

    public string Name { get; }

    public LayerMaskCommand(string name, Layer layer, byte[]? before, byte[]? after, Action? onChanged = null)
    {
        Name = name;
        _layer = layer;
        _before = before;
        _after = after;
        _onChanged = onChanged;
    }

    public void Execute()
    {
        _layer.SetMask((byte[]?)_after?.Clone());
        _onChanged?.Invoke();
    }

    public void Undo()
    {
        _layer.SetMask((byte[]?)_before?.Clone());
        _onChanged?.Invoke();
    }
}

/// <summary>
/// Put an entry (layer or group) into a group at an index and make it active. Undo takes it out
/// without disposing it, so later steps that hold the same entry (a mask stroke on it…) still
/// work after a redo.
/// </summary>
public sealed class InsertNodeCommand : IEditCommand
{
    private readonly PaintDocument _document;
    private readonly LayerNode _node;
    private readonly LayerGroup _parent;
    private readonly int _index;
    private readonly Guid? _activeBefore;
    private readonly Action? _onChanged;

    public string Name { get; }

    public InsertNodeCommand(string name, PaintDocument document, LayerNode node, LayerGroup parent, int index, Action? onChanged = null)
    {
        Name = name;
        _document = document;
        _node = node;
        _parent = parent;
        _index = index;
        _activeBefore = document.ActiveLayerId;
        _onChanged = onChanged;
    }

    public void Execute()
    {
        if (_node.Parent is null)
            _document.AddNode(_node, _parent, _index);
        _document.ActiveLayerId = _node.Id;
        _onChanged?.Invoke();
    }

    public void Undo()
    {
        _document.DetachNode(_node.Id);
        if (_activeBefore is Guid id && _document.FindNode(id) is not null)
            _document.ActiveLayerId = id;
        _onChanged?.Invoke();
    }
}

/// <summary>Delete a layer or a group (with its content); undo puts the same entry back.</summary>
public sealed class DeleteNodeCommand : IEditCommand
{
    private readonly PaintDocument _document;
    private readonly LayerNode _node;
    private LayerGroup? _parent;
    private int _index;
    private readonly Action? _onChanged;

    public string Name { get; }

    public DeleteNodeCommand(PaintDocument document, LayerNode node, Action? onChanged = null)
    {
        _document = document;
        _node = node;
        _onChanged = onChanged;
        Name = node is LayerGroup ? "Delete Group" : "Delete Layer";
    }

    public void Execute()
    {
        (_parent, _index) = _document.PositionOf(_node);
        _document.DetachNode(_node.Id);
        _onChanged?.Invoke();
    }

    public void Undo()
    {
        if (_parent is null) return;
        _document.AddNode(_node, _parent, _index);
        _onChanged?.Invoke();
    }
}

/// <summary>Move a layer or a group to another place in the stack (in or out of groups).</summary>
public sealed class MoveNodeCommand : IEditCommand
{
    private readonly PaintDocument _document;
    private readonly LayerNode _node;
    private readonly LayerGroup _fromParent;
    private readonly int _fromIndex;
    private readonly LayerGroup _toParent;
    private readonly int _toIndex;
    private readonly Action? _onChanged;

    public string Name { get; }

    /// <summary><paramref name="toIndex"/> is counted once the entry has left its current place.</summary>
    public MoveNodeCommand(string name, PaintDocument document, LayerNode node, LayerGroup toParent, int toIndex, Action? onChanged = null)
    {
        Name = name;
        _document = document;
        _node = node;
        (_fromParent, _fromIndex) = document.PositionOf(node);
        _toParent = toParent;
        _toIndex = toIndex;
        _onChanged = onChanged;
    }

    public void Execute()
    {
        _document.MoveNode(_node, _toParent, _toIndex);
        _onChanged?.Invoke();
    }

    public void Undo()
    {
        _document.MoveNode(_node, _fromParent, _fromIndex);
        _onChanged?.Invoke();
    }
}

/// <summary>Add, edit, switch, reorder or remove filters of a layer or group (the whole list before and after).</summary>
public sealed class NodeFiltersCommand : IEditCommand
{
    private readonly LayerNode _node;
    private readonly IReadOnlyList<LayerFilter> _before;
    private readonly IReadOnlyList<LayerFilter> _after;
    private readonly Action? _onChanged;

    public string Name { get; }

    public NodeFiltersCommand(string name, LayerNode node, IReadOnlyList<LayerFilter> before, IReadOnlyList<LayerFilter> after, Action? onChanged = null)
    {
        Name = name;
        _node = node;
        _before = before;
        _after = after;
        _onChanged = onChanged;
    }

    public void Execute()
    {
        _node.Filters = _after;
        _onChanged?.Invoke();
    }

    public void Undo()
    {
        _node.Filters = _before;
        _onChanged?.Invoke();
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
                (byte[]?)layer.Mask?.Clone(),
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

            layer.ReplacePixels(snap.Pixels.Clone(), (byte[]?)snap.Mask?.Clone());
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

    private sealed class LayerSnap(Guid id, PixelBuffer pixels, byte[]? mask, int offsetX, int offsetY)
    {
        public Guid Id { get; } = id;
        public PixelBuffer Pixels { get; } = pixels;
        public byte[]? Mask { get; } = mask;
        public int OffsetX { get; } = offsetX;
        public int OffsetY { get; } = offsetY;
    }
}
