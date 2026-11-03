using MiniPaint.Core.Composition;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Document;

/// <summary>
/// In-memory multi-layer document. Layers are ordered bottom → top.
/// </summary>
public sealed class PaintDocument : IDisposable
{
    private readonly List<Layer> _layers = new();
    private bool _disposed;

    public Guid Id { get; } = Guid.NewGuid();
    public int Width { get; private set; }
    public int Height { get; private set; }
    public string Name { get; set; }
    public string? FilePath { get; set; }
    public string? ProjectPath { get; set; }
    public bool IsDirty { get; set; }
    public Guid? ActiveLayerId { get; set; }

    public IReadOnlyList<Layer> Layers => _layers;

    public Layer? ActiveLayer =>
        ActiveLayerId is Guid id ? _layers.Find(l => l.Id == id) : null;

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
    /// New document sized to the layer's opaque pixel content (trimmed transparency).
    /// Returns null if the layer is fully transparent.
    /// </summary>
    public static PaintDocument? CreateFromLayerContent(Layer sourceLayer, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(sourceLayer);
        var cropped = PixelOps.CropToOpaqueContent(sourceLayer.Pixels, out _, out _);
        if (cropped is null) return null;

        string docName = string.IsNullOrWhiteSpace(name) ? sourceLayer.Name : name.Trim();
        var doc = new PaintDocument(docName, cropped.Width, cropped.Height);
        doc.AddLayer(new Layer(sourceLayer.Name, cropped, 0, 0)
        {
            IsVisible = true,
            Opacity = 1f,
            BlendMode = BlendMode.Normal
        });
        doc.IsDirty = true;
        return doc;
    }

    public Layer AddLayer(Layer layer, int? index = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(layer);
        if (index is null || index >= _layers.Count)
            _layers.Add(layer);
        else
            _layers.Insert(Math.Max(0, index.Value), layer);

        ActiveLayerId = layer.Id;
        IsDirty = true;
        return layer;
    }

    public Layer AddEmptyLayer(string name)
    {
        var layer = new Layer(name, Width, Height);
        return AddLayer(layer);
    }

    public bool RemoveLayer(Guid layerId)
    {
        ThrowIfDisposed();
        var idx = _layers.FindIndex(l => l.Id == layerId);
        if (idx < 0) return false;
        if (_layers.Count <= 1)
            throw new InvalidOperationException("Cannot delete the last layer.");

        var layer = _layers[idx];
        _layers.RemoveAt(idx);
        layer.Dispose();

        if (ActiveLayerId == layerId)
            ActiveLayerId = _layers[Math.Clamp(idx, 0, _layers.Count - 1)].Id;

        IsDirty = true;
        return true;
    }

    public Layer DuplicateLayer(Guid layerId)
    {
        ThrowIfDisposed();
        var idx = _layers.FindIndex(l => l.Id == layerId);
        if (idx < 0) throw new ArgumentException("Layer not found.", nameof(layerId));
        var copy = _layers[idx].Clone();
        _layers.Insert(idx + 1, copy);
        ActiveLayerId = copy.Id;
        IsDirty = true;
        return copy;
    }

    public void MoveLayer(Guid layerId, int newIndex)
    {
        ThrowIfDisposed();
        var idx = _layers.FindIndex(l => l.Id == layerId);
        if (idx < 0) throw new ArgumentException("Layer not found.", nameof(layerId));
        newIndex = Math.Clamp(newIndex, 0, _layers.Count - 1);
        if (idx == newIndex) return;

        var layer = _layers[idx];
        _layers.RemoveAt(idx);
        _layers.Insert(newIndex, layer);
        IsDirty = true;
    }

    public void RenameLayer(Guid layerId, string name)
    {
        ThrowIfDisposed();
        var layer = _layers.Find(l => l.Id == layerId)
            ?? throw new ArgumentException("Layer not found.", nameof(layerId));
        layer.Name = name;
        IsDirty = true;
    }

    public void SetLayerVisibility(Guid layerId, bool visible)
    {
        var layer = RequireLayer(layerId);
        layer.IsVisible = visible;
        IsDirty = true;
    }

    public void SetLayerOpacity(Guid layerId, float opacity)
    {
        var layer = RequireLayer(layerId);
        layer.Opacity = Math.Clamp(opacity, 0f, 1f);
        IsDirty = true;
    }

    public void SetLayerBlendMode(Guid layerId, Composition.BlendMode blendMode)
    {
        var layer = RequireLayer(layerId);
        layer.BlendMode = blendMode;
        IsDirty = true;
    }

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

    public Layer RequireLayer(Guid layerId)
    {
        ThrowIfDisposed();
        return _layers.Find(l => l.Id == layerId)
            ?? throw new ArgumentException("Layer not found.", nameof(layerId));
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var layer in _layers)
            layer.Dispose();
        _layers.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
