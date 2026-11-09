using MiniPaint.Core.ColorMode;
using MiniPaint.Core.Composition;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Document;

/// <summary>
/// In-memory multi-layer document. The layer stack is a tree: <see cref="Root"/> holds layers and
/// groups (which hold layers and groups…), each list ordered bottom → top.
/// </summary>
public sealed class PaintDocument : IDisposable
{
    private List<Layer>? _layers;
    private bool _disposed;

    public Guid Id { get; } = Guid.NewGuid();
    public int Width { get; private set; }
    public int Height { get; private set; }
    public string Name { get; set; }
    public string? FilePath { get; set; }
    public string? ProjectPath { get; set; }
    public bool IsDirty { get; set; }

    /// <summary>The active entry of the layer stack: a layer or a group.</summary>
    public Guid? ActiveLayerId { get; set; }

    /// <summary>Live color-mode constraint (RGBA / Indexed / ECS / AGA). Layers stay full RGBA.</summary>
    public DocumentColorSettings ColorSettings { get; } = new();

    /// <summary>The invisible top-level group: its children are the top-level entries.</summary>
    public LayerGroup Root { get; } = new("Root");

    /// <summary>Every pixel layer, groups flattened, bottom → top.</summary>
    public IReadOnlyList<Layer> Layers => _layers ??= AllNodes.OfType<Layer>().ToList();

    /// <summary>Every layer and group, depth-first, bottom → top (a group comes before its content).</summary>
    public IEnumerable<LayerNode> AllNodes => Root.SelfAndDescendants().Skip(1);

    public LayerNode? ActiveNode => ActiveLayerId is Guid id ? FindNode(id) : null;

    /// <summary>The active entry when it is a pixel layer; null when a group is active.</summary>
    public Layer? ActiveLayer => ActiveNode as Layer;

    public PaintDocument(string name, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Name = name;
        Width = width;
        Height = height;
    }

    public static PaintDocument FromImage(string path)
    {
        var pixels = ImageCodec.Load(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var doc = new PaintDocument(name, pixels.Width, pixels.Height)
        {
            FilePath = path
        };
        var layer = new Layer("Background", pixels);
        doc.AddLayer(layer);
        doc.IsDirty = false;
        return doc;
    }

    public static PaintDocument CreateBlank(string name, int width, int height, bool transparent = true)
    {
        var doc = new PaintDocument(name, width, height);
        var layer = new Layer("Background", width, height);
        if (!transparent)
            layer.Pixels.Clear(255, 255, 255, 255);
        doc.AddLayer(layer);
        return doc;
    }

    /// <summary>
    /// New document sized to the layer's visible pixel content (layer mask applied, transparency
    /// trimmed; its filters come along, still editable). Returns null if nothing of the layer shows.
    /// </summary>
    public static PaintDocument? CreateFromLayerContent(Layer sourceLayer, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(sourceLayer);
        PixelBuffer? cropped;
        if (sourceLayer.Mask is { } mask)
        {
            using var masked = sourceLayer.Pixels.Clone();
            LayerMask.ApplyTo(masked, mask);
            cropped = PixelOps.CropToOpaqueContent(masked, out _, out _);
        }
        else
        {
            cropped = PixelOps.CropToOpaqueContent(sourceLayer.Pixels, out _, out _);
        }
        if (cropped is null) return null;

        string docName = string.IsNullOrWhiteSpace(name) ? sourceLayer.Name : name.Trim();
        var doc = new PaintDocument(docName, cropped.Width, cropped.Height);
        doc.AddLayer(new Layer(sourceLayer.Name, cropped, 0, 0)
        {
            IsVisible = true,
            Opacity = 1f,
            BlendMode = BlendMode.Normal,
            Filters = sourceLayer.Filters
        });
        doc.IsDirty = true;
        return doc;
    }

    /// <summary>Add a layer at the top level (at <paramref name="index"/>, else on top) and make it active.</summary>
    public Layer AddLayer(Layer layer, int? index = null)
    {
        AddNode(layer, Root, index);
        return layer;
    }

    /// <summary>Put an entry into <paramref name="parent"/> (the root when null) at <paramref name="index"/> (else on top) and make it active.</summary>
    public void AddNode(LayerNode node, LayerGroup? parent = null, int? index = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(node);
        parent ??= Root;
        if (!ReferenceEquals(parent, Root) && !Root.Contains(parent))
            throw new ArgumentException("The group is not in this document.", nameof(parent));
        parent.Insert(index ?? parent.Children.Count, node);
        ActiveLayerId = node.Id;
        StructureChanged();
    }

    public bool RemoveNode(Guid nodeId)
    {
        var node = DetachNode(nodeId);
        node?.Dispose();
        return node is not null;
    }

    /// <summary>
    /// Take an entry out of the document without disposing it (an undo step can put the same
    /// instance back). Null when it is not in the document. The document always keeps at least
    /// one pixel layer.
    /// </summary>
    public LayerNode? DetachNode(Guid nodeId)
    {
        ThrowIfDisposed();
        var node = FindNode(nodeId);
        if (node?.Parent is not { } parent) return null;
        int layersInside = node.SelfAndDescendants().OfType<Layer>().Count();
        if (layersInside > 0 && layersInside >= Layers.Count)
            throw new InvalidOperationException("Cannot delete the last layer.");

        int index = parent.Remove(node);
        StructureChanged();
        if (ActiveLayerId is Guid active && node.SelfAndDescendants().Any(n => n.Id == active))
        {
            // Next active: the entry that took its place, else the one below, else its group.
            var siblings = parent.Children;
            ActiveLayerId = siblings.Count > 0
                ? siblings[Math.Clamp(index, 0, siblings.Count - 1)].Id
                : ReferenceEquals(parent, Root) ? Layers.LastOrDefault()?.Id : parent.Id;
        }
        return node;
    }

    /// <summary>Move an entry into <paramref name="parent"/> at <paramref name="index"/> (counted once it has left its place).</summary>
    public void MoveNode(LayerNode node, LayerGroup parent, int index)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(parent);
        if (node.Parent is not { } from)
            throw new ArgumentException("The entry is not in a group.", nameof(node));
        if (node is LayerGroup group && group.Contains(parent))
            throw new InvalidOperationException("A group cannot go inside itself.");
        from.Remove(node);
        parent.Insert(index, node);
        StructureChanged();
    }

    /// <summary>Where an entry sits: its group and its index there (bottom = 0).</summary>
    public (LayerGroup Parent, int Index) PositionOf(LayerNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Parent is not { } parent || !Root.Contains(node))
            throw new ArgumentException("The entry is not in this document.", nameof(node));
        return (parent, IndexIn(parent, node));
    }

    /// <summary>
    /// Where Move Up takes an entry: above the next one in its group, into the bottom of the group
    /// just above when that group is open, or out of its group when it is already on top.
    /// Null at the very top.
    /// </summary>
    public (LayerGroup Parent, int Index)? StepUp(LayerNode node)
    {
        var (parent, i) = PositionOf(node);
        if (i < parent.Children.Count - 1)
        {
            var next = parent.Children[i + 1];
            return next is LayerGroup { IsExpanded: true } group ? (group, 0) : (parent, i + 1);
        }
        if (parent.Parent is not { } outer) return null;
        return (outer, IndexIn(outer, parent) + 1);
    }

    /// <summary>Where Move Down takes an entry (the mirror of <see cref="StepUp"/>). Null at the very bottom.</summary>
    public (LayerGroup Parent, int Index)? StepDown(LayerNode node)
    {
        var (parent, i) = PositionOf(node);
        if (i > 0)
        {
            var below = parent.Children[i - 1];
            return below is LayerGroup { IsExpanded: true } group ? (group, group.Children.Count) : (parent, i - 1);
        }
        if (parent.Parent is not { } outer) return null;
        return (outer, IndexIn(outer, parent));
    }

    public LayerNode? FindNode(Guid id)
    {
        ThrowIfDisposed();
        foreach (var node in AllNodes)
        {
            if (node.Id == id)
                return node;
        }
        return null;
    }

    public LayerNode RequireNode(Guid id) =>
        FindNode(id) ?? throw new ArgumentException("Layer not found.", nameof(id));

    /// <summary>A pixel layer by id (groups do not count).</summary>
    public Layer RequireLayer(Guid layerId) =>
        FindNode(layerId) as Layer ?? throw new ArgumentException("Layer not found.", nameof(layerId));

    public void SetLayerOffset(Guid layerId, int x, int y)
    {
        var layer = RequireLayer(layerId);
        layer.OffsetX = x;
        layer.OffsetY = y;
        IsDirty = true;
    }

    /// <summary>Change document canvas dimensions (does not touch layer pixels).</summary>
    public void SetSize(int width, int height)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        IsDirty = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Root.Dispose();
        _layers = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static int IndexIn(LayerGroup parent, LayerNode node)
    {
        for (int i = 0; i < parent.Children.Count; i++)
        {
            if (ReferenceEquals(parent.Children[i], node))
                return i;
        }
        return -1;
    }

    private void StructureChanged()
    {
        _layers = null;
        IsDirty = true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
