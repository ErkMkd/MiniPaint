using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MiniPaint.App.Logging;
using MiniPaint.Core.Document;
using MiniPaint.Core.History;
using MiniPaint.Core.Transform;

namespace MiniPaint.App.Controls;

public enum CanvasTool
{
    Pan,
    RectSelect,
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

    private WriteableBitmap? _bitmap;
    private int _bitmapVersion = -1;
    private int _cacheVersion;
    private bool _panning;
    private bool _spaceDown;
    private bool _selecting;
    private bool _movingLayer;
    private Point _lastPointer;
    private int _selStartX, _selStartY;
    private int _moveStartDocX, _moveStartDocY;
    private int _layerStartOx, _layerStartOy;
    private DispatcherTimer? _antsTimer;
    private double _antsPhase;

    private ScaleInteraction? _scaleIx;
    private RotateInteraction? _rotateIx;
    private MoveInteraction? _moveIx;
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
            if (Workspace?.Selection.HasSelection == true || Workspace?.IsTransforming == true)
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
                _cacheVersion++;
            }
            InvalidateVisual();
            StatusChanged?.Invoke();
        }
    }

    private void OnWorkspaceChanged()
    {
        _cacheVersion++;
        InvalidateVisual();
        StatusChanged?.Invoke();
        TransformModeChanged?.Invoke();
    }

    public void InvalidateComposite()
    {
        _cacheVersion++;
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

        if (Tool == CanvasTool.RectSelect)
        {
            _selecting = true;
            (_selStartX, _selStartY) = ws.ScreenToDocument(p.X, p.Y);
            ws.Selection.Set(_selStartX, _selStartY, 0, 0);
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (Tool == CanvasTool.MoveLayer && ws.Document.ActiveLayer is { } layer)
        {
            _movingLayer = true;
            (_moveStartDocX, _moveStartDocY) = ws.ScreenToDocument(p.X, p.Y);
            _layerStartOx = layer.OffsetX;
            _layerStartOy = layer.OffsetY;
            e.Pointer.Capture(this);
            e.Handled = true;
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
            _scaleIx = session.BeginScale(hit, cdx, cdy, fromCenter: alt);
            _rotateIx = null;
            _moveIx = null;
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
                // Re-create with alt if needed mid-drag is awkward; alt is sampled at press.
                _scaleIx.Update(cdx, cdy, constrainProportions: shift);
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

        if (_selecting)
        {
            var (x, y) = ws.ScreenToDocument(p.X, p.Y);
            ws.Selection.Set(_selStartX, _selStartY, x - _selStartX, y - _selStartY);
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
        if (_selecting && ws is not null)
        {
            ws.Selection.ClampToDocument(ws.Document.Width, ws.Document.Height);
            InvalidateVisual();
            StatusChanged?.Invoke();
        }

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
        _movingLayer = false;
        _scaleIx = null;
        _rotateIx = null;
        _moveIx = null;
        _activeHandle = TransformHandle.None;
        e.Pointer.Capture(null);
        base.OnPointerReleased(e);
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

        if (ws.IsTransforming && ws.ActiveTransform is { } session)
            DrawTransformOverlay(context, ws, session);
        else if (ws.Selection.HasSelection)
            DrawMarchingAnts(context, ws);
    }

    private void EnsureBitmap(DocumentWorkspace ws)
    {
        if (_bitmap is not null &&
            _bitmap.PixelSize.Width == ws.Document.Width &&
            _bitmap.PixelSize.Height == ws.Document.Height &&
            _bitmapVersion == _cacheVersion)
            return;

        var composite = ws.GetComposite();
        var size = new PixelSize(composite.Width, composite.Height);
        if (_bitmap is null || _bitmap.PixelSize != size)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(size, new Vector(96, 96), Avalonia.Platform.PixelFormat.Rgba8888,
                Avalonia.Platform.AlphaFormat.Unpremul);
        }

        using (var fb = _bitmap.Lock())
        {
            var src = composite.PixelsReadOnly;
            System.Runtime.InteropServices.Marshal.Copy(src.ToArray(), 0, fb.Address, src.Length);
        }

        _bitmapVersion = _cacheVersion;
    }

    private void DrawCheckerboard(DrawingContext context, DocumentWorkspace ws)
    {
        double z = ws.Zoom;
        var dest = new Rect(ws.PanX, ws.PanY, ws.Document.Width * z, ws.Document.Height * z);
        const int cell = 8;
        var light = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        var dark = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
        context.FillRectangle(dark, dest);

        double screenCell = Math.Max(2, cell * z);
        int cols = (int)Math.Ceiling(dest.Width / screenCell);
        int rows = (int)Math.Ceiling(dest.Height / screenCell);
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                if (((x + y) & 1) == 0) continue;
                var r = new Rect(
                    dest.X + x * screenCell,
                    dest.Y + y * screenCell,
                    Math.Min(screenCell, dest.Right - (dest.X + x * screenCell)),
                    Math.Min(screenCell, dest.Bottom - (dest.Y + y * screenCell)));
                if (r.Width > 0 && r.Height > 0)
                    context.FillRectangle(light, r);
            }
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
        double x = ws.PanX + sel.X * ws.Zoom;
        double y = ws.PanY + sel.Y * ws.Zoom;
        double w = sel.Width * ws.Zoom;
        double h = sel.Height * ws.Zoom;
        var rect = new Rect(x, y, w, h);

        context.DrawRectangle(null, new Pen(Brushes.Black, 1), rect);
        context.DrawRectangle(null, new Pen(Brushes.White, 1)
        {
            DashStyle = new DashStyle(new double[] { 4, 4 }, _antsPhase)
        }, rect);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _antsTimer?.Stop();
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }
}
