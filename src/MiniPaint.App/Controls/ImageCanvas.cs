using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MiniPaint.App.Logging;
using MiniPaint.Core.Document;
using MiniPaint.Core.History;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Selection;
using MiniPaint.Core.Transform;

namespace MiniPaint.App.Controls;

public enum CanvasTool
{
    Pan,
    RectSelect,
    MagicWand,
    SelectionEdit,
    MoveLayer,
    FreeTransform
}

/// <summary>
/// Center viewport: checkerboard, composed image, zoom/pan, selection ants, free-transform handles.
/// </summary>
public sealed class ImageCanvas : Control
{
    public static readonly StyledProperty<DocumentWorkspace?> WorkspaceProperty =
        AvaloniaProperty.Register<ImageCanvas, DocumentWorkspace?>(nameof(Workspace));

    public static readonly StyledProperty<CanvasTool> ToolProperty =
        AvaloniaProperty.Register<ImageCanvas, CanvasTool>(nameof(Tool), CanvasTool.RectSelect);

    /// <summary>Options-bar mode for selection tools; Shift / Alt held at click time override it.</summary>
    public static readonly StyledProperty<SelectionCombineMode> SelectionModeProperty =
        AvaloniaProperty.Register<ImageCanvas, SelectionCombineMode>(nameof(SelectionMode));

    public static readonly StyledProperty<MagicWandOptions> WandOptionsProperty =
        AvaloniaProperty.Register<ImageCanvas, MagicWandOptions>(nameof(WandOptions), new MagicWandOptions());

    private WriteableBitmap? _bitmap;
    /// <summary>Workspace + composite version currently uploaded into <see cref="_bitmap"/>.</summary>
    private DocumentWorkspace? _bitmapWorkspace;
    private int _bitmapVersion = -1;
    private bool _panning;
    private bool _spaceDown;
    private bool _selecting;
    private bool _movingSelection;
    private bool _movingLayer;
    private Point _lastPointer;
    private int _selStartX, _selStartY;
    private int _selMoveOriginX, _selMoveOriginY;
    private int _selMoveStartDocX, _selMoveStartDocY;
    private int _moveStartDocX, _moveStartDocY;
    private int _layerStartOx, _layerStartOy;
    private DispatcherTimer? _antsTimer;
    private double _antsPhase;

    /// <summary>Marquee being dragged (doc coords) and how it combines with the selection on release.</summary>
    private int _selCurX, _selCurY;
    private SelectionCombineMode _selectMode;
    /// <summary>Selection before a Rectangular Selection gesture; the gesture is one undo step.</summary>
    private SelectionSnapshot? _selBefore;
    private string _selGestureName = "";
    private SelectionSnapshot? _floatSelBefore;

    /// <summary>Above this many outline segments the ants stop animating (redraw cost).</summary>
    private const int AntsAnimationSegmentLimit = 20_000;
    private SelectionState? _outlineSelection;
    private int _outlineShapeVersion = -1;
    private int _outlineCellSize;
    /// <summary>Outline segments of the selection shape, relative to its top-left corner.</summary>
    private IReadOnlyList<ContourSegment> _outlineSegments = [];
    private StreamGeometry? _outlineGeometry;
    private int _outlineVisibleCount;
    private (int selX, int selY, double zoom, double panX, double panY, Size bounds) _outlineView;

    /// <summary>Lifted selection pixels dragged with the marquee (Move tool).</summary>
    private PixelBuffer? _floatingPixels;
    private PixelBuffer? _floatingBefore;
    private WriteableBitmap? _floatingBitmap;
    private Layer? _floatingLayer;

    private ScaleInteraction? _scaleIx;
    private RotateInteraction? _rotateIx;
    private MoveInteraction? _moveIx;
    private SelectionScaleInteraction? _selScaleIx;
    private TransformHandle _activeHandle = TransformHandle.None;

    public DocumentWorkspace? Workspace
    {
        get => GetValue(WorkspaceProperty);
        set => SetValue(WorkspaceProperty, value);
    }

    public CanvasTool Tool
    {
        get => GetValue(ToolProperty);
        set => SetValue(ToolProperty, value);
    }

    public SelectionCombineMode SelectionMode
    {
        get => GetValue(SelectionModeProperty);
        set => SetValue(SelectionModeProperty, value);
    }

    public MagicWandOptions WandOptions
    {
        get => GetValue(WandOptionsProperty);
        set => SetValue(WandOptionsProperty, value);
    }

    public event Action? StatusChanged;
    public event Action? TransformModeChanged;

    public int CursorDocX { get; private set; }
    public int CursorDocY { get; private set; }

    static ImageCanvas()
    {
        AffectsRender<ImageCanvas>(WorkspaceProperty, ToolProperty);
        FocusableProperty.OverrideDefaultValue<ImageCanvas>(true);
    }

    public ImageCanvas()
    {
        ClipToBounds = true;
        _antsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _antsTimer.Tick += (_, _) =>
        {
            _antsPhase = (_antsPhase + 1) % 16;
            var ws = Workspace;
            if (_selecting ||
                ws?.IsTransforming == true ||
                ws?.IsEditingSelection == true ||
                (ws?.Selection.HasSelection == true &&
                 (ws.Selection.IsRectangular || _outlineVisibleCount <= AntsAnimationSegmentLimit)))
                InvalidateVisual();
        };
        _antsTimer.Start();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WorkspaceProperty)
        {
            if (change.OldValue is DocumentWorkspace oldWs)
                oldWs.Changed -= OnWorkspaceChanged;
            if (change.NewValue is DocumentWorkspace newWs)
            {
                newWs.Changed += OnWorkspaceChanged;
            }
            InvalidateVisual();
            StatusChanged?.Invoke();
        }
    }

    private void OnWorkspaceChanged()
    {
        InvalidateVisual();
        StatusChanged?.Invoke();
        TransformModeChanged?.Invoke();
    }

    public void InvalidateComposite()
    {
        Workspace?.InvalidateComposite();
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var ws = Workspace;
        if (ws?.IsTransforming == true)
        {
            if (e.Key == Key.Enter)
            {
                ws.CommitTransform();
                TransformModeChanged?.Invoke();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                ws.CancelTransform();
                TransformModeChanged?.Invoke();
                e.Handled = true;
                return;
            }
        }

        if (ws?.IsEditingSelection == true)
        {
            if (e.Key == Key.Enter)
            {
                ws.CommitSelectionEdit();
                TransformModeChanged?.Invoke();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                ws.CancelSelectionEdit();
                TransformModeChanged?.Invoke();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Escape && _floatingPixels is not null)
        {
            CancelFloatingSelection(ws);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            _spaceDown = true;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _spaceDown = false;
            e.Handled = true;
        }
        base.OnKeyUp(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var ws = Workspace;
        if (ws is null) return;

        var p = e.GetPosition(this);
        double factor = e.Delta.Y > 0 ? 1.1 : 1.0 / 1.1;
        ws.SetZoomAt(ws.Zoom * factor, p.X, p.Y);
        UpdateCursorDoc(p);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var ws = Workspace;
        if (ws is null) return;

        var p = e.GetPosition(this);
        _lastPointer = p;
        UpdateCursorDoc(p);

        var props = e.GetCurrentPoint(this).Properties;
        bool middle = props.IsMiddleButtonPressed;
        bool left = props.IsLeftButtonPressed;

        if (middle || (left && (_spaceDown || Tool == CanvasTool.Pan)))
        {
            _panning = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!left) return;

        if (ws.IsTransforming && ws.ActiveTransform is { } session)
        {
            BeginTransformInteraction(ws, session, p, e.KeyModifiers);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (Tool == CanvasTool.FreeTransform)
        {
            if (ws.BeginTransform())
            {
                TransformModeChanged?.Invoke();
                InvalidateVisual();
            }
            e.Handled = true;
            return;
        }

        if (Tool == CanvasTool.SelectionEdit || ws.IsEditingSelection)
        {
            if (!ws.IsEditingSelection && !ws.BeginSelectionEdit())
            {
                e.Handled = true;
                return;
            }
            BeginSelectionEditInteraction(ws, p, e.KeyModifiers);
            e.Pointer.Capture(this);
            e.Handled = true;
            TransformModeChanged?.Invoke();
            InvalidateVisual();
            return;
        }

        if (Tool == CanvasTool.MagicWand)
        {
            var (docX, docY) = ws.ScreenToDocument(p.X, p.Y);
            try
            {
                ws.MagicWandSelect(docX, docY, WandOptions, CombineModeFrom(e.KeyModifiers));
            }
            catch (Exception ex)
            {
                AppLog.Error("Magic Wand failed", ex);
            }
            e.Handled = true;
            InvalidateVisual();
            StatusChanged?.Invoke();
            return;
        }

        if (Tool == CanvasTool.RectSelect)
        {
            var (docX, docY) = ws.ScreenToDocument(p.X, p.Y);
            var mode = CombineModeFrom(e.KeyModifiers);
            _selBefore = ws.Selection.Capture();

            // Photoshop: plain drag inside the selection moves the outline (pixels stay).
            if (mode == SelectionCombineMode.Replace && ws.Selection.Contains(docX, docY))
            {
                _selGestureName = "Move Selection Outline";
                _movingSelection = true;
                _selMoveStartDocX = docX;
                _selMoveStartDocY = docY;
                _selMoveOriginX = ws.Selection.X;
                _selMoveOriginY = ws.Selection.Y;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            // New marquee. Replace drops the old selection now (so a click without drag deselects);
            // Shift / Alt / Shift+Alt combine with it on release.
            _selGestureName = "Rectangular Marquee";
            _selectMode = mode;
            if (mode == SelectionCombineMode.Replace)
                ws.Selection.Clear();
            _selecting = true;
            _selStartX = _selCurX = docX;
            _selStartY = _selCurY = docY;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (Tool == CanvasTool.MoveLayer && ws.Document.ActiveLayer is { } layer)
        {
            var (docX, docY) = ws.ScreenToDocument(p.X, p.Y);

            // Selection present → lift selected pixels and drag them with the marquee.
            if (ws.Selection.HasSelection)
            {
                if (!BeginFloatingSelection(ws, layer, docX, docY))
                {
                    e.Handled = true;
                    return;
                }

                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            _movingLayer = true;
            _moveStartDocX = docX;
            _moveStartDocY = docY;
            _layerStartOx = layer.OffsetX;
            _layerStartOy = layer.OffsetY;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    private bool BeginFloatingSelection(DocumentWorkspace ws, Layer layer, int docX, int docY)
    {
        if (_floatingPixels is not null)
            return true; // already floating (shouldn't happen mid-gesture)

        var before = layer.Pixels.Clone();
        var lifted = SelectionOps.CutFromActiveLayer(ws.Document, ws.Selection);
        if (lifted is null)
        {
            before.Dispose();
            return false;
        }

        _floatSelBefore = ws.Selection.Capture();
        _floatingBefore = before;
        _floatingPixels = lifted;
        _floatingLayer = layer;
        RebuildFloatingBitmap();
        ws.InvalidateComposite();

        _movingSelection = true;
        _selMoveStartDocX = docX;
        _selMoveStartDocY = docY;
        _selMoveOriginX = ws.Selection.X;
        _selMoveOriginY = ws.Selection.Y;
        InvalidateVisual();
        StatusChanged?.Invoke();
        return true;
    }

    private void RebuildFloatingBitmap()
    {
        _floatingBitmap?.Dispose();
        _floatingBitmap = null;
        if (_floatingPixels is null) return;

        var size = new PixelSize(_floatingPixels.Width, _floatingPixels.Height);
        _floatingBitmap = new WriteableBitmap(size, new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);
        using var fb = _floatingBitmap.Lock();
        _floatingPixels.CopyTo(fb.Address, fb.RowBytes);
    }

    private void CommitFloatingSelection(DocumentWorkspace ws)
    {
        if (_floatingPixels is null || _floatingLayer is null || _floatingBefore is null)
        {
            ClearFloatingState();
            return;
        }

        bool moved = ws.Selection.HasSelection &&
                     (ws.Selection.X != _selMoveOriginX || ws.Selection.Y != _selMoveOriginY);
        bool discarded = !ws.Selection.HasSelection;

        if (!moved && !discarded)
        {
            // Click without drag — restore exact pixels, no history entry.
            _floatingLayer.ReplacePixels(_floatingBefore.Clone());
            ClearFloatingState();
            ws.InvalidateComposite();
            ws.NotifyChanged();
            return;
        }

        if (discarded)
        {
            // Marquee left the document entirely — keep the cut (pixels discarded).
            var afterEmpty = _floatingLayer.Pixels.Clone();
            ws.History.Record(new LayerPixelsCommand(
                "Move Selection", _floatingLayer, _floatingBefore, afterEmpty, ws.NotifyChanged));
            _floatingBefore = null; // owned by command
            ClearFloatingState(disposeBefore: false);
            ws.NotifyChanged();
            return;
        }

        PixelOps.PasteOver(
            _floatingLayer.Pixels,
            _floatingLayer.OffsetX,
            _floatingLayer.OffsetY,
            _floatingPixels,
            ws.Selection.X,
            ws.Selection.Y);
        ws.Document.IsDirty = true;
        ws.Selection.ClampToDocument(ws.Document.Width, ws.Document.Height);

        // Pixels and marquee move together, so one undo puts both back.
        var after = _floatingLayer.Pixels.Clone();
        IEditCommand pixels = new LayerPixelsCommand(
            "Move Selection", _floatingLayer, _floatingBefore, after, ws.NotifyChanged);
        ws.History.Record(_floatSelBefore is null
            ? pixels
            : new CompoundCommand("Move Selection", pixels,
                new SelectionCommand("Move Selection", ws.Selection, _floatSelBefore, ws.Selection.Capture())));
        _floatingBefore = null;
        ClearFloatingState(disposeBefore: false);
        ws.NotifyChanged();
    }

    private void CancelFloatingSelection(DocumentWorkspace? ws)
    {
        if (_floatingLayer is not null && _floatingBefore is not null)
        {
            _floatingLayer.ReplacePixels(_floatingBefore.Clone());
            if (ws is not null)
            {
                if (_floatSelBefore is not null)
                    ws.Selection.Restore(_floatSelBefore);
                else
                    ws.Selection.MoveTo(_selMoveOriginX, _selMoveOriginY);
                ws.NotifyChanged();
            }
        }

        ClearFloatingState();
        _movingSelection = false;
        InvalidateVisual();
        StatusChanged?.Invoke();
    }

    private void ClearFloatingState(bool disposeBefore = true)
    {
        _floatingPixels?.Dispose();
        _floatingPixels = null;
        if (disposeBefore)
            _floatingBefore?.Dispose();
        _floatingBefore = null;
        _floatingBitmap?.Dispose();
        _floatingBitmap = null;
        _floatingLayer = null;
        _floatSelBefore = null;
    }

    private void BeginSelectionEditInteraction(DocumentWorkspace ws, Point p, KeyModifiers mods)
    {
        var edit = ws.ActiveSelectionEdit;
        if (edit is null) return;

        double cdx = (p.X - ws.PanX) / ws.Zoom;
        double cdy = (p.Y - ws.PanY) / ws.Zoom;
        double handleRadiusDoc = 6.0 / ws.Zoom;
        var hit = edit.HitTest(cdx, cdy, handleRadiusDoc);
        bool alt = mods.HasFlag(KeyModifiers.Alt);
        bool shift = mods.HasFlag(KeyModifiers.Shift);

        if (hit is not TransformHandle.None and not TransformHandle.Move)
        {
            _activeHandle = hit;
            _selScaleIx = edit.BeginScale(hit);
            _selScaleIx.Update(cdx, cdy, constrainProportions: shift, fromCenter: alt);
            InvalidateVisual();
            return;
        }

        if (hit == TransformHandle.Move || ws.Selection.BoundsContainDoc(cdx, cdy))
        {
            _movingSelection = true;
            var (docX, docY) = ws.ScreenToDocument(p.X, p.Y);
            _selMoveStartDocX = docX;
            _selMoveStartDocY = docY;
            _selMoveOriginX = ws.Selection.X;
            _selMoveOriginY = ws.Selection.Y;
            _selScaleIx = null;
        }
    }

    private void BeginTransformInteraction(
        DocumentWorkspace ws, FreeTransformSession session, Point p, KeyModifiers mods)
    {
        var (docX, docY) = ws.ScreenToDocument(p.X, p.Y);
        // Continuous doc coords for smoother transform
        double cdx = (p.X - ws.PanX) / ws.Zoom;
        double cdy = (p.Y - ws.PanY) / ws.Zoom;

        double handleRadiusDoc = 6.0 / ws.Zoom;
        var hit = session.HitTest(cdx, cdy, handleRadiusDoc);
        bool alt = mods.HasFlag(KeyModifiers.Alt);

        if (hit is not TransformHandle.None and not TransformHandle.Move)
        {
            _activeHandle = hit;
            _scaleIx = session.BeginScale(hit);
            _rotateIx = null;
            _moveIx = null;
            // Apply immediately so Alt state at press is respected from first move.
            _scaleIx.Update(cdx, cdy, constrainProportions: mods.HasFlag(KeyModifiers.Shift), fromCenter: alt);
            return;
        }

        if (session.HitTestRotate(cdx, cdy, handleRadiusDoc, handleRadiusDoc * 4))
        {
            _activeHandle = TransformHandle.Rotate;
            _rotateIx = session.BeginRotate(cdx, cdy);
            _scaleIx = null;
            _moveIx = null;
            return;
        }

        // Also allow rotate when near corners outside the box (screen-space)
        if (NearCornerOutside(session, ws, p, out _))
        {
            _activeHandle = TransformHandle.Rotate;
            _rotateIx = session.BeginRotate(cdx, cdy);
            _scaleIx = null;
            _moveIx = null;
            return;
        }

        if (hit == TransformHandle.Move || FreeTransformSession.PointInQuad(cdx, cdy, session.GetCorners()))
        {
            _activeHandle = TransformHandle.Move;
            _moveIx = session.BeginMove(cdx, cdy);
            _scaleIx = null;
            _rotateIx = null;
        }
    }

    private bool NearCornerOutside(FreeTransformSession session, DocumentWorkspace ws, Point screen, out TransformHandle corner)
    {
        corner = TransformHandle.None;
        TransformHandle[] corners =
            [TransformHandle.NW, TransformHandle.NE, TransformHandle.SE, TransformHandle.SW];
        foreach (var h in corners)
        {
            var (dx, dy) = session.GetHandleDocPoint(h);
            double sx = dx * ws.Zoom + ws.PanX;
            double sy = dy * ws.Zoom + ws.PanY;
            double dist = Math.Sqrt((screen.X - sx) * (screen.X - sx) + (screen.Y - sy) * (screen.Y - sy));
            if (dist > 8 && dist < 28)
            {
                corner = h;
                return true;
            }
        }
        return false;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var ws = Workspace;
        var p = e.GetPosition(this);
        UpdateCursorDoc(p);

        if (ws is null) return;

        if (_panning)
        {
            var delta = p - _lastPointer;
            ws.PanX += delta.X;
            ws.PanY += delta.Y;
            _lastPointer = p;
            InvalidateVisual();
            StatusChanged?.Invoke();
            e.Handled = true;
            return;
        }

        if (ws.IsTransforming && ws.ActiveTransform is not null &&
            (_scaleIx is not null || _rotateIx is not null || _moveIx is not null))
        {
            double cdx = (p.X - ws.PanX) / ws.Zoom;
            double cdy = (p.Y - ws.PanY) / ws.Zoom;
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

            if (_scaleIx is not null)
            {
                // Alt live (Photoshop): opposite handle by default, center while Alt is held.
                _scaleIx.Update(cdx, cdy, constrainProportions: shift, fromCenter: alt);
            }
            else if (_rotateIx is not null)
            {
                _rotateIx.Update(cdx, cdy, snap15: shift);
            }
            else if (_moveIx is not null)
            {
                _moveIx.Update(cdx, cdy, constrainAxis: shift);
            }

            try
            {
                ws.RefreshTransformPreview();
            }
            catch (Exception ex)
            {
                AppLog.Error("RefreshTransformPreview failed during drag", ex);
                _scaleIx = null;
                _rotateIx = null;
                _moveIx = null;
            }
            e.Handled = true;
            return;
        }

        if (_selScaleIx is not null && ws.IsEditingSelection)
        {
            double cdx = (p.X - ws.PanX) / ws.Zoom;
            double cdy = (p.Y - ws.PanY) / ws.Zoom;
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            _selScaleIx.Update(cdx, cdy, constrainProportions: shift, fromCenter: alt);
            InvalidateVisual();
            StatusChanged?.Invoke();
            e.Handled = true;
            return;
        }

        if (_movingSelection && ws.Selection.HasSelection)
        {
            var (x, y) = ws.ScreenToDocument(p.X, p.Y);
            int nx = _selMoveOriginX + (x - _selMoveStartDocX);
            int ny = _selMoveOriginY + (y - _selMoveStartDocY);
            ws.Selection.MoveTo(nx, ny);
            InvalidateVisual();
            StatusChanged?.Invoke();
            e.Handled = true;
            return;
        }

        if (_selecting)
        {
            (_selCurX, _selCurY) = ws.ScreenToDocument(p.X, p.Y);
            InvalidateVisual();
            StatusChanged?.Invoke();
            e.Handled = true;
            return;
        }

        if (_movingLayer && ws.Document.ActiveLayer is { } layer)
        {
            var (x, y) = ws.ScreenToDocument(p.X, p.Y);
            layer.OffsetX = _layerStartOx + (x - _moveStartDocX);
            layer.OffsetY = _layerStartOy + (y - _moveStartDocY);
            ws.InvalidateComposite();
            InvalidateVisual();
            StatusChanged?.Invoke();
            e.Handled = true;
        }

        // Cursor feedback while hovering transform handles
        if (ws.IsTransforming && ws.ActiveTransform is { } session)
        {
            UpdateTransformCursor(session, ws, p);
        }
        else if (ws.IsEditingSelection && ws.ActiveSelectionEdit is { } edit)
        {
            UpdateSelectionEditCursor(edit, ws, p);
        }
        else if (Tool == CanvasTool.RectSelect && ws.Selection.HasSelection)
        {
            var (dx, dy) = ws.ScreenToDocument(p.X, p.Y);
            Cursor = ws.Selection.Contains(dx, dy)
                ? new Cursor(StandardCursorType.SizeAll)
                : Cursor.Default;
        }
        else if (Tool == CanvasTool.MoveLayer)
        {
            Cursor = new Cursor(StandardCursorType.SizeAll);
        }
        else if (Tool == CanvasTool.MagicWand)
        {
            Cursor = new Cursor(StandardCursorType.Cross);
        }
    }

    private void UpdateSelectionEditCursor(SelectionEditSession edit, DocumentWorkspace ws, Point p)
    {
        double cdx = (p.X - ws.PanX) / ws.Zoom;
        double cdy = (p.Y - ws.PanY) / ws.Zoom;
        double handleRadiusDoc = 6.0 / ws.Zoom;
        var hit = edit.HitTest(cdx, cdy, handleRadiusDoc);
        Cursor = hit is not TransformHandle.None
            ? new Cursor(StandardCursorType.SizeAll)
            : Cursor.Default;
    }

    private void UpdateTransformCursor(FreeTransformSession session, DocumentWorkspace ws, Point p)
    {
        double cdx = (p.X - ws.PanX) / ws.Zoom;
        double cdy = (p.Y - ws.PanY) / ws.Zoom;
        double handleRadiusDoc = 6.0 / ws.Zoom;
        var hit = session.HitTest(cdx, cdy, handleRadiusDoc);
        if (hit is not TransformHandle.None and not TransformHandle.Move)
            Cursor = new Cursor(StandardCursorType.SizeAll);
        else if (session.HitTestRotate(cdx, cdy, handleRadiusDoc, handleRadiusDoc * 4) ||
                 NearCornerOutside(session, ws, p, out _))
            Cursor = new Cursor(StandardCursorType.TopRightCorner);
        else if (hit == TransformHandle.Move)
            Cursor = new Cursor(StandardCursorType.SizeAll);
        else
            Cursor = Cursor.Default;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var ws = Workspace;

        // Commit floating pixels before clamping the marquee (clamp may clear an off-canvas sel).
        if (_floatingPixels is not null && ws is not null)
            CommitFloatingSelection(ws);

        if (_selecting && ws is not null)
            ApplyMarquee(ws);

        if ((_selecting || _movingSelection || _selScaleIx is not null) && ws is not null)
        {
            ws.Selection.ClampToDocument(ws.Document.Width, ws.Document.Height);
            // Rectangular Selection gestures are one undo step each (Transform Selection records on Apply).
            if (_selBefore is not null)
                ws.RecordSelectionChange(_selGestureName, _selBefore);
            InvalidateVisual();
            StatusChanged?.Invoke();
        }
        _selBefore = null;

        if (_movingLayer && ws?.Document.ActiveLayer is { } layer)
        {
            int endX = layer.OffsetX;
            int endY = layer.OffsetY;
            layer.OffsetX = _layerStartOx;
            layer.OffsetY = _layerStartOy;
            if (endX != _layerStartOx || endY != _layerStartOy)
            {
                ws.History.Execute(LayerPropertyCommand.Move(layer, endX, endY, ws.NotifyChanged));
            }
        }

        _panning = false;
        _selecting = false;
        _movingSelection = false;
        _movingLayer = false;
        _scaleIx = null;
        _rotateIx = null;
        _moveIx = null;
        _selScaleIx = null;
        _activeHandle = TransformHandle.None;
        e.Pointer.Capture(null);
        base.OnPointerReleased(e);
    }

    private SelectionCombineMode CombineModeFrom(KeyModifiers mods)
    {
        bool shift = mods.HasFlag(KeyModifiers.Shift);
        bool alt = mods.HasFlag(KeyModifiers.Alt);
        return (shift, alt) switch
        {
            (true, true) => SelectionCombineMode.Intersect,
            (true, false) => SelectionCombineMode.Add,
            (false, true) => SelectionCombineMode.Subtract,
            _ => SelectionMode
        };
    }

    /// <summary>Combine the dragged marquee into the selection (a click without drag adds nothing).</summary>
    private void ApplyMarquee(DocumentWorkspace ws)
    {
        var marquee = new SelectionState();
        marquee.Set(_selStartX, _selStartY, _selCurX - _selStartX, _selCurY - _selStartY);
        marquee.ClampToDocument(ws.Document.Width, ws.Document.Height);
        if (marquee.HasSelection)
            ws.Selection.Combine(marquee, _selectMode);
    }

    private void UpdateCursorDoc(Point p)
    {
        if (Workspace is null) return;
        (CursorDocX, CursorDocY) = Workspace.ScreenToDocument(p.X, p.Y);
        StatusChanged?.Invoke();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), bounds);

        var ws = Workspace;
        if (ws is null) return;

        EnsureBitmap(ws);
        if (_bitmap is null) return;

        DrawCheckerboard(context, ws);

        var dest = new Rect(ws.PanX, ws.PanY, ws.Document.Width * ws.Zoom, ws.Document.Height * ws.Zoom);
        var renderOpts = new RenderOptions
        {
            BitmapInterpolationMode = ws.Zoom >= 4.0
                ? BitmapInterpolationMode.None
                : BitmapInterpolationMode.HighQuality
        };
        using (context.PushRenderOptions(renderOpts))
        {
            context.DrawImage(_bitmap, new Rect(0, 0, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height), dest);
        }

        if (_floatingBitmap is not null && _floatingPixels is not null && ws.Selection.HasSelection)
            DrawFloatingSelection(context, ws);

        if (ws.IsTransforming && ws.ActiveTransform is { } session)
            DrawTransformOverlay(context, ws, session);
        else if (ws.IsEditingSelection && ws.ActiveSelectionEdit is { } edit)
            DrawSelectionEditOverlay(context, ws, edit);
        else if (ws.Selection.HasSelection)
            DrawMarchingAnts(context, ws);

        if (_selecting)
            DrawMarquee(context, ws);
    }

    private void DrawFloatingSelection(DrawingContext context, DocumentWorkspace ws)
    {
        if (_floatingBitmap is null || _floatingPixels is null) return;

        var sel = ws.Selection;
        var dest = new Rect(
            ws.PanX + sel.X * ws.Zoom,
            ws.PanY + sel.Y * ws.Zoom,
            sel.Width * ws.Zoom,
            sel.Height * ws.Zoom);
        var renderOpts = new RenderOptions
        {
            BitmapInterpolationMode = ws.Zoom >= 4.0
                ? BitmapInterpolationMode.None
                : BitmapInterpolationMode.HighQuality
        };
        using (context.PushRenderOptions(renderOpts))
        {
            context.DrawImage(
                _floatingBitmap,
                new Rect(0, 0, _floatingBitmap.PixelSize.Width, _floatingBitmap.PixelSize.Height),
                dest);
        }
    }

    private void EnsureBitmap(DocumentWorkspace ws)
    {
        // GetComposite is cached: selection, zoom and pan changes reuse the uploaded bitmap;
        // only a recomputed composite (new CompositeVersion) is copied again.
        var composite = ws.GetComposite();
        var size = new PixelSize(composite.Width, composite.Height);
        if (_bitmap is not null && _bitmap.PixelSize == size &&
            ReferenceEquals(_bitmapWorkspace, ws) && _bitmapVersion == ws.CompositeVersion)
            return;

        if (_bitmap is null || _bitmap.PixelSize != size)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(size, new Vector(96, 96), Avalonia.Platform.PixelFormat.Rgba8888,
                Avalonia.Platform.AlphaFormat.Unpremul);
        }

        using (var fb = _bitmap.Lock())
            composite.CopyTo(fb.Address, fb.RowBytes);

        _bitmapWorkspace = ws;
        _bitmapVersion = ws.CompositeVersion;
    }

    private const int CheckerLight = unchecked((int)0xFF555555);
    private const int CheckerDark = unchecked((int)0xFF3A3A3A);
    private WriteableBitmap? _checkerBitmap;
    private (int x, int y, int w, int h, double scale, double cell, double panX, double panY) _checkerKey;

    /// <summary>
    /// Transparency checkerboard (8 document px cells, at least 2 screen px) under the visible part
    /// of the document. Rendered into a cached bitmap at device resolution and rebuilt only when
    /// zoom, pan or size change — redrawing it cell by cell cost tens of thousands of rectangles per frame.
    /// </summary>
    private void DrawCheckerboard(DrawingContext context, DocumentWorkspace ws)
    {
        double z = ws.Zoom;
        var docRect = new Rect(ws.PanX, ws.PanY, ws.Document.Width * z, ws.Document.Height * z);
        var visible = docRect.Intersect(new Rect(Bounds.Size));
        if (visible.Width <= 0 || visible.Height <= 0) return;

        int x0 = (int)Math.Floor(visible.X);
        int y0 = (int)Math.Floor(visible.Y);
        int w = (int)Math.Ceiling(visible.Right) - x0;
        int h = (int)Math.Ceiling(visible.Bottom) - y0;
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        double cell = Math.Max(2, 8 * z);

        var key = (x0, y0, w, h, scale, cell, ws.PanX, ws.PanY);
        if (_checkerBitmap is null || _checkerKey != key)
        {
            RenderCheckerboard(x0, y0, w, h, scale, cell, ws.PanX, ws.PanY);
            _checkerKey = key;
        }

        using (context.PushClip(visible))
            context.DrawImage(_checkerBitmap!,
                new Rect(0, 0, _checkerBitmap!.PixelSize.Width, _checkerBitmap.PixelSize.Height),
                new Rect(x0, y0, w, h));
    }

    private void RenderCheckerboard(int x0, int y0, int w, int h, double scale, double cell, double panX, double panY)
    {
        int pw = Math.Max(1, (int)Math.Ceiling(w * scale));
        int ph = Math.Max(1, (int)Math.Ceiling(h * scale));
        if (_checkerBitmap is null || _checkerBitmap.PixelSize != new PixelSize(pw, ph))
        {
            _checkerBitmap?.Dispose();
            _checkerBitmap = new WriteableBitmap(new PixelSize(pw, ph), new Vector(96 * scale, 96 * scale),
                Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        }

        // Cell index of each device-pixel column, measured from the document origin on screen.
        var columnCell = new int[pw];
        for (int px = 0; px < pw; px++)
            columnCell[px] = (int)Math.Floor((x0 + (px + 0.5) / scale - panX) / cell);

        var row = new int[pw];
        using var fb = _checkerBitmap.Lock();
        for (int py = 0; py < ph; py++)
        {
            int rowCell = (int)Math.Floor((y0 + (py + 0.5) / scale - panY) / cell);
            for (int px = 0; px < pw; px++)
                row[px] = ((columnCell[px] + rowCell) & 1) == 0 ? CheckerDark : CheckerLight;
            System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + py * fb.RowBytes, pw);
        }
    }

    private void DrawTransformOverlay(DrawingContext context, DocumentWorkspace ws, FreeTransformSession session)
    {
        var corners = session.GetCorners();
        var pts = corners.Select(c => new Point(c.x * ws.Zoom + ws.PanX, c.y * ws.Zoom + ws.PanY)).ToArray();

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(pts[0], false);
            g.LineTo(pts[1]);
            g.LineTo(pts[2]);
            g.LineTo(pts[3]);
            g.EndFigure(true);
        }

        context.DrawGeometry(null, new Pen(Brushes.Black, 1), geo);
        context.DrawGeometry(null, new Pen(Brushes.White, 1)
        {
            DashStyle = new DashStyle(new double[] { 4, 4 }, _antsPhase)
        }, geo);

        TransformHandle[] handles =
        [
            TransformHandle.NW, TransformHandle.N, TransformHandle.NE,
            TransformHandle.E, TransformHandle.SE, TransformHandle.S,
            TransformHandle.SW, TransformHandle.W
        ];

        foreach (var h in handles)
        {
            var (dx, dy) = session.GetHandleDocPoint(h);
            double sx = dx * ws.Zoom + ws.PanX;
            double sy = dy * ws.Zoom + ws.PanY;
            var rect = new Rect(sx - 4, sy - 4, 8, 8);
            context.FillRectangle(Brushes.White, rect);
            context.DrawRectangle(null, new Pen(Brushes.Black, 1), rect);
        }

        // Pivot marker
        var (cx, cy) = session.GetHandleDocPoint(TransformHandle.Move);
        double px = cx * ws.Zoom + ws.PanX;
        double py = cy * ws.Zoom + ws.PanY;
        context.DrawLine(new Pen(Brushes.White, 1), new Point(px - 5, py), new Point(px + 5, py));
        context.DrawLine(new Pen(Brushes.White, 1), new Point(px, py - 5), new Point(px, py + 5));
        context.DrawLine(new Pen(Brushes.Black, 1), new Point(px - 5, py + 1), new Point(px + 5, py + 1));
    }

    private void DrawMarchingAnts(DrawingContext context, DocumentWorkspace ws)
    {
        var sel = ws.Selection;
        var bounds = DocToScreen(ws, sel.X, sel.Y, sel.Width, sel.Height);
        if (sel.IsRectangular)
        {
            DrawAntsRect(context, bounds);
            return;
        }

        var geometry = GetOutlineGeometry(ws);
        if (geometry is null)
        {
            // Nothing is ≥ 50% selected (very soft mask): show the bounds instead.
            DrawAntsRect(context, bounds);
            return;
        }

        context.DrawGeometry(null, new Pen(Brushes.Black, 1), geometry);
        context.DrawGeometry(null, new Pen(Brushes.White, 1)
        {
            DashStyle = new DashStyle(new double[] { 4, 4 }, _antsPhase)
        }, geometry);
    }

    /// <summary>
    /// Outline of a masked selection in screen coordinates. Segments are recomputed only when the
    /// shape changes (not while it is dragged); the geometry is rebuilt when position, zoom or pan change.
    /// </summary>
    private StreamGeometry? GetOutlineGeometry(DocumentWorkspace ws)
    {
        var sel = ws.Selection;
        int cellSize = OutlineCellSize(ws.Zoom);
        if (!ReferenceEquals(_outlineSelection, sel) || _outlineShapeVersion != sel.ShapeVersion ||
            _outlineCellSize != cellSize)
        {
            _outlineSegments = SelectionContour.Build(sel, cellSize)
                .Select(s => new ContourSegment(s.X0 - sel.X, s.Y0 - sel.Y, s.X1 - sel.X, s.Y1 - sel.Y))
                .ToList();
            _outlineSelection = sel;
            _outlineShapeVersion = sel.ShapeVersion;
            _outlineCellSize = cellSize;
            _outlineGeometry = null;
        }
        if (_outlineSegments.Count == 0)
            return null;

        var view = (sel.X, sel.Y, ws.Zoom, ws.PanX, ws.PanY, Bounds.Size);
        if (_outlineGeometry is not null && _outlineView == view)
            return _outlineGeometry;

        // Only segments on screen go into the geometry (zoomed-in fragmented selections).
        double ox = ws.PanX + sel.X * ws.Zoom;
        double oy = ws.PanY + sel.Y * ws.Zoom;
        double right = Bounds.Width + 1;
        double bottom = Bounds.Height + 1;
        int visible = 0;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            foreach (var s in _outlineSegments)
            {
                double x0 = ox + s.X0 * ws.Zoom, y0 = oy + s.Y0 * ws.Zoom;
                double x1 = ox + s.X1 * ws.Zoom, y1 = oy + s.Y1 * ws.Zoom;
                if (Math.Max(x0, x1) < -1 || Math.Min(x0, x1) > right ||
                    Math.Max(y0, y1) < -1 || Math.Min(y0, y1) > bottom)
                    continue;
                g.BeginFigure(new Point(x0, y0), false);
                g.LineTo(new Point(x1, y1));
                g.EndFigure(false);
                visible++;
            }
        }
        _outlineGeometry = geometry;
        _outlineView = view;
        _outlineVisibleCount = visible;
        return geometry;
    }

    /// <summary>Zoomed out, trace the outline on cells of about one screen pixel (power of two).</summary>
    private static int OutlineCellSize(double zoom)
    {
        int cell = 1;
        while (cell < 64 && cell * 2 <= 1.0 / zoom)
            cell *= 2;
        return cell;
    }

    /// <summary>Rectangle being dragged with Rectangular Selection.</summary>
    private void DrawMarquee(DrawingContext context, DocumentWorkspace ws)
    {
        int x0 = Math.Min(_selStartX, _selCurX);
        int y0 = Math.Min(_selStartY, _selCurY);
        int w = Math.Abs(_selCurX - _selStartX);
        int h = Math.Abs(_selCurY - _selStartY);
        if (w == 0 || h == 0) return;
        DrawAntsRect(context, DocToScreen(ws, x0, y0, w, h));
    }

    private void DrawAntsRect(DrawingContext context, Rect rect)
    {
        context.DrawRectangle(null, new Pen(Brushes.Black, 1), rect);
        context.DrawRectangle(null, new Pen(Brushes.White, 1)
        {
            DashStyle = new DashStyle(new double[] { 4, 4 }, _antsPhase)
        }, rect);
    }

    private static Rect DocToScreen(DocumentWorkspace ws, double x, double y, double width, double height) =>
        new(ws.PanX + x * ws.Zoom, ws.PanY + y * ws.Zoom, width * ws.Zoom, height * ws.Zoom);

    private void DrawSelectionEditOverlay(DrawingContext context, DocumentWorkspace ws, SelectionEditSession edit)
    {
        DrawMarchingAnts(context, ws);

        TransformHandle[] handles =
        [
            TransformHandle.NW, TransformHandle.N, TransformHandle.NE,
            TransformHandle.E, TransformHandle.SE, TransformHandle.S,
            TransformHandle.SW, TransformHandle.W
        ];

        foreach (var h in handles)
        {
            var (dx, dy) = edit.GetHandleDocPoint(h);
            double sx = dx * ws.Zoom + ws.PanX;
            double sy = dy * ws.Zoom + ws.PanY;
            var rect = new Rect(sx - 4, sy - 4, 8, 8);
            context.FillRectangle(Brushes.White, rect);
            context.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 1), rect);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _antsTimer?.Stop();
        _bitmap?.Dispose();
        _bitmap = null;
        _bitmapWorkspace = null;
        _checkerBitmap?.Dispose();
        _checkerBitmap = null;
        ClearFloatingState();
        base.OnDetachedFromVisualTree(e);
    }
}
