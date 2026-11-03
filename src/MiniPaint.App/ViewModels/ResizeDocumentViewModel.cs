using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniPaint.Core.Document;

namespace MiniPaint.App.ViewModels;

public enum ResizeDialogMode
{
    CanvasSize,
    ImageSize
}

public partial class ResizeDocumentViewModel : ViewModelBase
{
    private readonly int _originalWidth;
    private readonly int _originalHeight;
    private bool _syncing;

    public ResizeDialogMode Mode { get; }

    public string Title => Mode == ResizeDialogMode.CanvasSize ? "Canvas Size" : "Image Size";

    public string Subtitle => Mode == ResizeDialogMode.CanvasSize
        ? "Change the workspace size without scaling layer pixels (crop / transparent extend)."
        : "Scale all layer pixels to the new document size.";

    public string InterpolationNote =>
        $"Interpolation: {DocumentResize.ImageInterpolationName}.";

    public bool IsCanvasMode => Mode == ResizeDialogMode.CanvasSize;
    public bool IsImageMode => Mode == ResizeDialogMode.ImageSize;

    [ObservableProperty] public partial decimal WidthValue { get; set; }
    [ObservableProperty] public partial decimal HeightValue { get; set; }
    [ObservableProperty] public partial bool ConstrainProportions { get; set; } = true;
    [ObservableProperty] public partial CanvasAnchor Anchor { get; set; } = CanvasAnchor.Center;

    public int ResultWidth { get; private set; }
    public int ResultHeight { get; private set; }

    public string AnchorLabel => Anchor switch
    {
        CanvasAnchor.NorthWest => "Top Left",
        CanvasAnchor.North => "Top",
        CanvasAnchor.NorthEast => "Top Right",
        CanvasAnchor.West => "Left",
        CanvasAnchor.Center => "Center",
        CanvasAnchor.East => "Right",
        CanvasAnchor.SouthWest => "Bottom Left",
        CanvasAnchor.South => "Bottom",
        CanvasAnchor.SouthEast => "Bottom Right",
        _ => Anchor.ToString()
    };

    private static readonly IBrush Selected = new SolidColorBrush(Color.Parse("#0E639C"));
    private static readonly IBrush Normal = new SolidColorBrush(Color.Parse("#3F3F46"));

    public IBrush AnchorNorthWestBrush => BrushFor(CanvasAnchor.NorthWest);
    public IBrush AnchorNorthBrush => BrushFor(CanvasAnchor.North);
    public IBrush AnchorNorthEastBrush => BrushFor(CanvasAnchor.NorthEast);
    public IBrush AnchorWestBrush => BrushFor(CanvasAnchor.West);
    public IBrush AnchorCenterBrush => BrushFor(CanvasAnchor.Center);
    public IBrush AnchorEastBrush => BrushFor(CanvasAnchor.East);
    public IBrush AnchorSouthWestBrush => BrushFor(CanvasAnchor.SouthWest);
    public IBrush AnchorSouthBrush => BrushFor(CanvasAnchor.South);
    public IBrush AnchorSouthEastBrush => BrushFor(CanvasAnchor.SouthEast);

    public ResizeDocumentViewModel(ResizeDialogMode mode, int width, int height)
    {
        Mode = mode;
        _originalWidth = Math.Max(1, width);
        _originalHeight = Math.Max(1, height);
        WidthValue = _originalWidth;
        HeightValue = _originalHeight;
    }

    partial void OnWidthValueChanged(decimal value)
    {
        if (_syncing || !ConstrainProportions || Mode != ResizeDialogMode.ImageSize) return;
        _syncing = true;
        HeightValue = Math.Max(1, Math.Round(value * _originalHeight / _originalWidth));
        _syncing = false;
    }

    partial void OnHeightValueChanged(decimal value)
    {
        if (_syncing || !ConstrainProportions || Mode != ResizeDialogMode.ImageSize) return;
        _syncing = true;
        WidthValue = Math.Max(1, Math.Round(value * _originalWidth / _originalHeight));
        _syncing = false;
    }

    partial void OnAnchorChanged(CanvasAnchor value)
    {
        OnPropertyChanged(nameof(AnchorLabel));
        OnPropertyChanged(nameof(AnchorNorthWestBrush));
        OnPropertyChanged(nameof(AnchorNorthBrush));
        OnPropertyChanged(nameof(AnchorNorthEastBrush));
        OnPropertyChanged(nameof(AnchorWestBrush));
        OnPropertyChanged(nameof(AnchorCenterBrush));
        OnPropertyChanged(nameof(AnchorEastBrush));
        OnPropertyChanged(nameof(AnchorSouthWestBrush));
        OnPropertyChanged(nameof(AnchorSouthBrush));
        OnPropertyChanged(nameof(AnchorSouthEastBrush));
    }

    public bool TryCommit(out string? error)
    {
        error = null;
        int w = (int)Math.Clamp(Math.Round(WidthValue), 1, 16384);
        int h = (int)Math.Clamp(Math.Round(HeightValue), 1, 16384);
        if (w < 1 || h < 1)
        {
            error = "Size must be at least 1×1.";
            return false;
        }

        ResultWidth = w;
        ResultHeight = h;
        return true;
    }

    private IBrush BrushFor(CanvasAnchor a) => Anchor == a ? Selected : Normal;
}
