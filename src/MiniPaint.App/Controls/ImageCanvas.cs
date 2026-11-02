using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MiniPaint.Core.Document;
using MiniPaint.Core.History;
using MiniPaint.Core.Imaging;

namespace MiniPaint.App.Controls;

public enum CanvasTool
{
    Pan,
    RectSelect,
    MoveLayer
}

/// <summary>
/// Center viewport: checkerboard, composed image, zoom/pan, selection ants, tool hit-testing.
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
            if (Workspace?.Selection.HasSelection == true)
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
    }

    public void InvalidateComposite()
    {
        _cacheVersion++;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
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
            ws.InvalidateComposite();
            // Pan doesn't dirty document — fire view change only
            InvalidateVisual();
            StatusChanged?.Invoke();
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
            // Restore start then apply via history command
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
        var interp = ws.Zoom >= 4.0
            ? BitmapInterpolationMode.None
            : BitmapInterpolationMode.HighQuality;

        // Avalonia RenderOptions via RenderOptions.SetBitmapInterpolationMode on control is set below.
        RenderOptions.SetBitmapInterpolationMode(this, interp);
        context.DrawImage(_bitmap, new Rect(0, 0, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height), dest);

        if (ws.Selection.HasSelection)
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

        // Draw only visible checker cells at screen scale (cell*zoom)
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

    private void DrawMarchingAnts(DrawingContext context, DocumentWorkspace ws)
    {
        var sel = ws.Selection;
        double x = ws.PanX + sel.X * ws.Zoom;
        double y = ws.PanY + sel.Y * ws.Zoom;
        double w = sel.Width * ws.Zoom;
        double h = sel.Height * ws.Zoom;
        var rect = new Rect(x, y, w, h);

        var black = new Pen(Brushes.Black, 1);
        var white = new Pen(Brushes.White, 1);
        // Static dual stroke + dashed overlay for "ants"
        context.DrawRectangle(null, black, rect);
        var dash = new Pen(Brushes.White, 1)
        {
            DashStyle = new DashStyle(new double[] { 4, 4 }, _antsPhase)
        };
        context.DrawRectangle(null, dash, rect);
        _ = white;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _antsTimer?.Stop();
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }
}
