using MiniPaint.Core.Clipboard;
using MiniPaint.Core.ColorMode;
using MiniPaint.Core.Composition;
using MiniPaint.Core.History;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Project;
using MiniPaint.Core.Selection;
using MiniPaint.Core.Transform;

namespace MiniPaint.Core.Document;

/// <summary>
/// One open document tab: document + selection + view transform + history + composite cache.
/// </summary>
public sealed class DocumentWorkspace : IDisposable
{
    private PixelBuffer? _compositeCache;
    private bool _compositeDirty = true;
    private bool _disposed;

    /// <summary>Source layer when Free Transform is lifting a selection (float is not in Layers).</summary>
    private Layer? _selectionTransformSource;
    private PixelBuffer? _selectionTransformBefore;
    private SelectionSnapshot? _selectionBeforeTransform;
    /// <summary>Selection coverage as an RGBA buffer, baked with the float so the marquee follows it.</summary>
    private PixelBuffer? _selectionTransformMask;

    public PaintDocument Document { get; }
    public SelectionState Selection { get; } = new();
    public HistoryStack History { get; } = new();
    public FreeTransformSession? ActiveTransform { get; private set; }
    public bool IsTransforming => ActiveTransform is not null;

    /// <summary>True when Free Transform is operating on lifted selection pixels (not the whole layer).</summary>
    public bool IsSelectionTransform => _selectionTransformSource is not null;

    public SelectionEditSession? ActiveSelectionEdit { get; private set; }
    public bool IsEditingSelection => ActiveSelectionEdit is not null;

    public double Zoom { get; private set; } = 1.0;
    public double PanX { get; set; }
    public double PanY { get; set; }

    public string TabTitle => Document.IsDirty ? Document.Name + "*" : Document.Name;

    /// <summary>Incremented each time <see cref="GetComposite"/> recomputes the image (the canvas re-uploads only then).</summary>
    public int CompositeVersion { get; private set; }

    public event Action? Changed;

    public DocumentWorkspace(PaintDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        History.Changed += OnHistoryChanged;
    }

    private void OnHistoryChanged()
    {
        // Selection-only steps leave pixels alone: no recomposite, document stays saved.
        if (History.LastAffected is SelectionCommand)
            Changed?.Invoke();
        else
            NotifyChanged();
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
            // Selection Free Transform floats above the document (hole already cut in source).
            if (IsSelectionTransform && ActiveTransform is { Layer: { } floatLayer })
            {
                PixelOps.BlendLayer(
                    _compositeCache,
                    floatLayer.Pixels,
                    floatLayer.OffsetX,
                    floatLayer.OffsetY,
                    floatLayer.Opacity,
                    floatLayer.BlendMode);
            }
            if (Document.ColorSettings.Mode != ColorModeKind.Rgba)
                ColorModePipeline.ApplyInPlace(_compositeCache, Document.ColorSettings);
            _compositeDirty = false;
            CompositeVersion++;
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
        var before = Selection.Capture();
        Selection.SelectAll(Document.Width, Document.Height);
        RecordSelectionChange("Select All", before);
    }

    public void Deselect()
    {
        if (IsTransforming) return;
        if (IsEditingSelection)
            CancelSelectionEdit();
        var before = Selection.Capture();
        Selection.Clear();
        RecordSelectionChange("Deselect", before);
    }

    public void InvertSelection()
    {
        if (IsTransforming || !Selection.HasSelection) return;
        if (IsEditingSelection)
            CancelSelectionEdit();
        var before = Selection.Capture();
        Selection.Invert(Document.Width, Document.Height);
        RecordSelectionChange("Inverse", before);
    }

    /// <summary>
    /// Magic Wand click at a document pixel, combined with the current selection (one undo step).
    /// </summary>
    public void MagicWandSelect(int docX, int docY, MagicWandOptions options, SelectionCombineMode mode)
    {
        if (IsTransforming) return;
        if ((uint)docX >= (uint)Document.Width || (uint)docY >= (uint)Document.Height) return;
        if (IsEditingSelection)
            CancelSelectionEdit();

        SelectionState picked;
        if (options.SampleVisibleImage)
        {
            picked = MagicWand.Select(GetComposite(), docX, docY, options);
        }
        else
        {
            var layer = Document.ActiveLayer;
            if (layer is null) return;
            using var layerOnCanvas = PixelOps.ExtractRegion(
                layer.Pixels, layer.OffsetX, layer.OffsetY, 0, 0, Document.Width, Document.Height);
            picked = MagicWand.Select(layerOnCanvas, docX, docY, options);
        }

        var before = Selection.Capture();
        Selection.Combine(picked, mode);
        RecordSelectionChange("Magic Wand", before);
    }

    /// <summary>
    /// Select → Color Range on the visible image, combined with the current selection (one undo step).
    /// </summary>
    public void ColorRangeSelect(ColorRangeOptions options, SelectionCombineMode mode)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (IsTransforming) return;
        if (IsEditingSelection)
            CancelSelectionEdit();

        var picked = ColorRange.Select(GetComposite(), options);
        var before = Selection.Capture();
        Selection.Combine(picked, mode);
        RecordSelectionChange("Color Range", before);
    }

    /// <summary>
    /// Record the selection change made since <paramref name="before"/> as one undo step
    /// (nothing is recorded when the selection did not actually change).
    /// </summary>
    public void RecordSelectionChange(string name, SelectionSnapshot before)
    {
        ArgumentNullException.ThrowIfNull(before);
        var after = Selection.Capture();
        if (after.SameAs(before))
        {
            Changed?.Invoke();
            return;
        }
        History.Record(new SelectionCommand(name, Selection, before, after));
    }

    public bool BeginSelectionEdit()
    {
        if (IsTransforming) return false;
        if (ActiveSelectionEdit is not null) return true;
        if (!Selection.HasSelection) return false;
        ActiveSelectionEdit = new SelectionEditSession(Selection);
        Changed?.Invoke();
        return true;
    }

    public void CommitSelectionEdit()
    {
        if (ActiveSelectionEdit is null) return;
        Selection.ClampToDocument(Document.Width, Document.Height);
        var before = ActiveSelectionEdit.Original;
        ActiveSelectionEdit = null;
        RecordSelectionChange("Transform Selection", before);
    }

    public void CancelSelectionEdit()
    {
        if (ActiveSelectionEdit is null) return;
        ActiveSelectionEdit.RestoreOriginal();
        ActiveSelectionEdit = null;
        Changed?.Invoke();
    }

    public void ExportPng(string path)
    {
        ProjectSerializer.ExportPng(Document, path);
    }

    public void ExportJpeg(string path, int quality)
    {
        ProjectSerializer.ExportJpeg(Document, path, quality);
    }

    public void SaveProject(string path)
    {
        ProjectSerializer.Save(Document, path);
        MarkSaved();
    }

    public bool BeginTransform()
    {
        if (ActiveTransform is not null) return true;
        if (IsEditingSelection)
            CancelSelectionEdit();
        var layer = Document.ActiveLayer;
        if (layer is null) return false;

        if (Selection.HasSelection)
            return BeginSelectionTransform(layer);

        ActiveTransform = new FreeTransformSession(layer);
        Selection.Clear();
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Lift selection pixels (like Move), free-transform the float, keep the marquee on the quad AABB.
    /// </summary>
    private bool BeginSelectionTransform(Layer sourceLayer)
    {
        var before = sourceLayer.Pixels.Clone();
        var mask = SelectionOps.MaskToPixelBuffer(Selection);
        var lifted = SelectionOps.CutFromActiveLayer(Document, Selection);
        if (lifted is null)
        {
            before.Dispose();
            mask.Dispose();
            return false;
        }

        _selectionBeforeTransform = Selection.Capture();
        _selectionTransformMask = mask;
        _selectionTransformSource = sourceLayer;
        _selectionTransformBefore = before;

        // Orphan layer: composited in GetComposite, not listed in Document.Layers.
        var floatLayer = new Layer("Selection", lifted, Selection.X, Selection.Y);
        ActiveTransform = new FreeTransformSession(floatLayer);
        SyncSelectionToTransformBounds();
        InvalidateComposite();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Rebake layer pixels from the frozen original using current transform params.</summary>
    public void RefreshTransformPreview()
    {
        if (ActiveTransform is null) return;
        var (pixels, ox, oy) = ActiveTransform.Bake();
        ActiveTransform.Layer.ReplacePixels(pixels);
        ActiveTransform.Layer.OffsetX = ox;
        ActiveTransform.Layer.OffsetY = oy;
        if (IsSelectionTransform)
            SyncSelectionToTransformBounds();
        InvalidateComposite();
        Changed?.Invoke();
    }

    public void CancelTransform()
    {
        if (ActiveTransform is null) return;
        var session = ActiveTransform;

        if (IsSelectionTransform)
        {
            var source = _selectionTransformSource!;
            var before = _selectionTransformBefore!;
            source.ReplacePixels(before.Clone());
            Selection.Restore(_selectionBeforeTransform!);
            ActiveTransform = null;
            session.Layer.Dispose();
            session.Dispose();
            ClearSelectionTransformState(disposeBefore: true);
            InvalidateComposite();
            Changed?.Invoke();
            return;
        }

        session.Layer.ReplacePixels(session.OriginalPixels.Clone());
        session.Layer.OffsetX = session.OriginalOffsetX;
        session.Layer.OffsetY = session.OriginalOffsetY;
        ActiveTransform = null;
        session.Dispose();
        InvalidateComposite();
        Changed?.Invoke();
    }

    public void CommitTransform()
    {
        if (ActiveTransform is null) return;
        RefreshTransformPreview();
        var session = ActiveTransform;

        if (IsSelectionTransform)
        {
            var source = _selectionTransformSource!;
            var before = _selectionTransformBefore!;
            var floatLayer = session.Layer;

            PixelOps.PasteOver(
                source.Pixels,
                source.OffsetX,
                source.OffsetY,
                floatLayer.Pixels,
                floatLayer.OffsetX,
                floatLayer.OffsetY);
            var after = source.Pixels.Clone();

            // The marquee takes the transformed shape of the lifted selection.
            var (maskPixels, maskX, maskY) = session.Bake(_selectionTransformMask!);
            using (maskPixels)
                Selection.SetMask(maskX, maskY, maskPixels.Width, maskPixels.Height, AlphaChannel(maskPixels));
            Selection.ClampToDocument(Document.Width, Document.Height);
            var selectionBefore = _selectionBeforeTransform!;

            ActiveTransform = null;
            floatLayer.Dispose();
            session.Dispose();
            _selectionTransformBefore = null; // owned by history command
            ClearSelectionTransformState(disposeBefore: false);

            History.Record(new CompoundCommand(
                "Transform Selection",
                new LayerPixelsCommand("Transform Selection", source, before, after, NotifyChanged),
                new SelectionCommand("Transform Selection", Selection, selectionBefore, Selection.Capture())));
            Document.IsDirty = true;
            Changed?.Invoke();
            return;
        }

        var layer = session.Layer;
        var layerBefore = session.OriginalPixels.Clone();
        int beforeOx = session.OriginalOffsetX;
        int beforeOy = session.OriginalOffsetY;
        var layerAfter = layer.Pixels.Clone();
        int afterOx = layer.OffsetX;
        int afterOy = layer.OffsetY;

        ActiveTransform = null;
        session.Dispose();

        History.Record(new TransformLayerCommand(
            layer, layerBefore, beforeOx, beforeOy, layerAfter, afterOx, afterOy, NotifyChanged));
        Document.IsDirty = true;
        Changed?.Invoke();
    }

    private void SyncSelectionToTransformBounds()
    {
        if (ActiveTransform is null) return;
        var layer = ActiveTransform.Layer;
        Selection.Set(layer.OffsetX, layer.OffsetY, layer.Pixels.Width, layer.Pixels.Height);
    }

    private void ClearSelectionTransformState(bool disposeBefore)
    {
        if (disposeBefore)
            _selectionTransformBefore?.Dispose();
        _selectionTransformBefore = null;
        _selectionTransformSource = null;
        _selectionBeforeTransform = null;
        _selectionTransformMask?.Dispose();
        _selectionTransformMask = null;
    }

    private static byte[] AlphaChannel(PixelBuffer buffer)
    {
        var px = buffer.PixelsReadOnly;
        var alpha = new byte[buffer.Width * buffer.Height];
        for (int i = 0; i < alpha.Length; i++)
            alpha[i] = px[i * 4 + 3];
        return alpha;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (ActiveTransform is not null)
            CancelTransform();
        if (ActiveSelectionEdit is not null)
            CancelSelectionEdit();
        _compositeCache?.Dispose();
        Document.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
