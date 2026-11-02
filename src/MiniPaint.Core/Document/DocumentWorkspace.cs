using MiniPaint.Core.Clipboard;
using MiniPaint.Core.Composition;
using MiniPaint.Core.History;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Project;
using MiniPaint.Core.Selection;

namespace MiniPaint.Core.Document;

/// <summary>
/// One open document tab: document + selection + view transform + history + composite cache.
/// </summary>
public sealed class DocumentWorkspace : IDisposable
{
    private PixelBuffer? _compositeCache;
    private bool _compositeDirty = true;
    private bool _disposed;

    public PaintDocument Document { get; }
    public SelectionState Selection { get; } = new();
    public HistoryStack History { get; } = new();

    public double Zoom { get; private set; } = 1.0;
    public double PanX { get; set; }
    public double PanY { get; set; }

    public string TabTitle => Document.IsDirty ? Document.Name + "*" : Document.Name;

    public event Action? Changed;

    public DocumentWorkspace(PaintDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        History.Changed += () => NotifyChanged();
    }

    public void NotifyChanged()
    {
        _compositeDirty = true;
        Document.IsDirty = true;
        Changed?.Invoke();
    }

    public void MarkSaved()
    {
        Document.IsDirty = false;
        Changed?.Invoke();
    }

    public PixelBuffer GetComposite()
    {
        if (_compositeCache is null ||
            _compositeCache.Width != Document.Width ||
            _compositeCache.Height != Document.Height)
        {
            _compositeCache?.Dispose();
            _compositeCache = new PixelBuffer(Document.Width, Document.Height);
            _compositeDirty = true;
        }

        if (_compositeDirty)
        {
            CompositionEngine.CompositeInto(Document, _compositeCache);
            _compositeDirty = false;
        }

        return _compositeCache;
    }

    public void InvalidateComposite() => _compositeDirty = true;

    public void SetZoomAt(double newZoom, double cursorScreenX, double cursorScreenY)
    {
        newZoom = Math.Clamp(newZoom, 0.05, 64.0);
        var (panX, panY) = PixelOps.ZoomAtCursor(Zoom, newZoom, cursorScreenX, cursorScreenY, PanX, PanY);
        Zoom = newZoom;
        PanX = panX;
        PanY = panY;
        Changed?.Invoke();
    }

    public void SetZoomAbsolute(double zoom)
    {
        Zoom = Math.Clamp(zoom, 0.05, 64.0);
        Changed?.Invoke();
    }

    public void FitToWindow(double viewportWidth, double viewportHeight, double margin = 16)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0) return;
        double availW = Math.Max(1, viewportWidth - margin * 2);
        double availH = Math.Max(1, viewportHeight - margin * 2);
        double zx = availW / Document.Width;
        double zy = availH / Document.Height;
        Zoom = Math.Clamp(Math.Min(zx, zy), 0.05, 64.0);
        PanX = (viewportWidth - Document.Width * Zoom) / 2.0;
        PanY = (viewportHeight - Document.Height * Zoom) / 2.0;
        Changed?.Invoke();
    }

    public void Zoom100(double viewportWidth, double viewportHeight)
    {
        Zoom = 1.0;
        PanX = (viewportWidth - Document.Width) / 2.0;
        PanY = (viewportHeight - Document.Height) / 2.0;
        Changed?.Invoke();
    }

    public (int x, int y) ScreenToDocument(double screenX, double screenY) =>
        PixelOps.ScreenToDocument(screenX, screenY, PanX, PanY, Zoom);

    public void Copy(ImageClipboard clipboard)
    {
        var pixels = SelectionOps.CopyFromActiveLayer(Document, Selection);
        if (pixels is null) return;
        clipboard.Set(pixels, Selection.X, Selection.Y);
    }

    public void Cut(ImageClipboard clipboard)
    {
        var layer = Document.ActiveLayer;
        if (layer is null || !Selection.HasSelection) return;

        var before = layer.Pixels.Clone();
        var pixels = SelectionOps.CutFromActiveLayer(Document, Selection);
        if (pixels is null)
        {
            before.Dispose();
            return;
        }

        clipboard.Set(pixels, Selection.X, Selection.Y);
        var after = layer.Pixels.Clone();
        History.Record(new LayerPixelsCommand("Cut", layer, before, after, NotifyChanged));
        NotifyChanged();
    }

    public Layer? Paste(ImageClipboard clipboard, int? atX = null, int? atY = null)
    {
        var clone = clipboard.PeekClone();
        if (clone is null) return null;

        int x = atX ?? clipboard.OriginX;
        int y = atY ?? clipboard.OriginY;
        var cmd = PasteLayerCommand.CreatePending(
            Document, $"Pasted {Document.Layers.Count + 1}", clone, x, y, NotifyChanged);
        History.Execute(cmd);
        return cmd.LastLayer;
    }

    public void SelectAll()
    {
        Selection.SelectAll(Document.Width, Document.Height);
        Changed?.Invoke();
    }

    public void Deselect()
    {
        Selection.Clear();
        Changed?.Invoke();
    }

    public void ExportPng(string path)
    {
        ProjectSerializer.ExportPng(Document, path);
    }

    public void SaveProject(string path)
    {
        ProjectSerializer.Save(Document, path);
        MarkSaved();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _compositeCache?.Dispose();
        Document.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
