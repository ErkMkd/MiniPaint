using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Selection;

namespace MiniPaint.App.ViewModels;

/// <summary>
/// Select → Color Range. Colors are picked from the full-resolution image; the live preview is
/// computed on a nearest-sampled thumbnail (exact colors, instant), the final selection on the
/// full image when the dialog is accepted.
/// </summary>
public partial class ColorRangeViewModel : ViewModelBase, IDisposable
{
    private const int MaxPreviewWidth = 480;
    private const int MaxPreviewHeight = 320;

    private static readonly IBrush IncludedOutline = new SolidColorBrush(Color.Parse("#CCCCCC"));
    private static readonly IBrush ExcludedOutline = new SolidColorBrush(Color.Parse("#F48771"));

    /// <summary>Visible image (document composite) — not owned.</summary>
    private readonly PixelBuffer? _source;
    private readonly PixelBuffer? _thumbnail;
    private readonly List<SampledColor> _include = new();
    private readonly List<SampledColor> _exclude = new();

    public SelectionModeOption[] Modes { get; } =
    [
        new(SelectionCombineMode.Replace, "New selection"),
        new(SelectionCombineMode.Add, "Add to selection"),
        new(SelectionCombineMode.Subtract, "Subtract from selection"),
        new(SelectionCombineMode.Intersect, "Intersect with selection")
    ];

    [ObservableProperty] public partial SelectionModeOption SelectedMode { get; set; }
    [ObservableProperty] public partial decimal FuzzinessValue { get; set; }
    [ObservableProperty] public partial bool ShowSelectionPreview { get; set; } = true;
    [ObservableProperty] public partial WriteableBitmap? Preview { get; set; }

    public ObservableCollection<ColorSwatch> Swatches { get; } = new();

    public string Title => "Color Range";
    public double PreviewWidth { get; }
    public double PreviewHeight { get; }

    public int Fuzziness => (int)Math.Clamp(Math.Round(FuzzinessValue), 0, 255);

    /// <summary>Slider mirror of <see cref="FuzzinessValue"/> (Slider is double, NumericUpDown decimal).</summary>
    public double FuzzinessSliderValue
    {
        get => (double)FuzzinessValue;
        set => FuzzinessValue = Math.Clamp(Math.Round((decimal)value), 0, 255);
    }

    public bool ShowImagePreview
    {
        get => !ShowSelectionPreview;
        set => ShowSelectionPreview = !value;
    }

    public bool HasColors => _include.Count > 0;

    public string StatusText => _include.Count == 0
        ? "No color sampled yet."
        : $"{_include.Count} color(s) sampled" + (_exclude.Count > 0 ? $", {_exclude.Count} excluded." : ".");

    public ColorRangeOptions Options => new(_include.ToArray(), _exclude.ToArray(), Fuzziness);

    public ColorRangeViewModel() : this(null, 40, SelectionCombineMode.Replace)
    {
    }

    public ColorRangeViewModel(PixelBuffer? source, int fuzziness, SelectionCombineMode mode)
    {
        _source = source;
        if (source is not null)
        {
            double scale = Math.Min(MaxPreviewWidth / (double)source.Width, MaxPreviewHeight / (double)source.Height);
            int w = Math.Max(1, (int)Math.Round(source.Width * scale));
            int h = Math.Max(1, (int)Math.Round(source.Height * scale));
            PreviewWidth = w;
            PreviewHeight = h;
            _thumbnail = PixelOps.ResampleNearest(source, w, h);
        }

        SelectedMode = Modes.First(m => m.Mode == mode);
        FuzzinessValue = Math.Clamp(fuzziness, 0, 255);
        RefreshPreview();
    }

    /// <summary>
    /// Sample the image at a point of the preview (0–1 on each axis). Plain click: that color only;
    /// Shift: add it; Alt: remove it (or exclude it when it was not one of the sampled colors).
    /// </summary>
    public void PickAt(double fx, double fy, bool add, bool subtract)
    {
        if (_source is null) return;
        int x = (int)Math.Clamp(fx * _source.Width, 0, _source.Width - 1);
        int y = (int)Math.Clamp(fy * _source.Height, 0, _source.Height - 1);
        var color = ColorRange.SampleAt(_source, x, y);

        if (subtract)
        {
            if (!_include.Remove(color) && !_exclude.Contains(color))
                _exclude.Add(color);
        }
        else if (add)
        {
            _exclude.Remove(color);
            if (!_include.Contains(color))
                _include.Add(color);
        }
        else
        {
            _include.Clear();
            _exclude.Clear();
            _include.Add(color);
        }

        RefreshSwatches();
        OnPropertyChanged(nameof(HasColors));
        OnPropertyChanged(nameof(StatusText));
        RefreshPreview();
    }

    partial void OnFuzzinessValueChanged(decimal value)
    {
        OnPropertyChanged(nameof(FuzzinessSliderValue));
        RefreshPreview();
    }

    partial void OnShowSelectionPreviewChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowImagePreview));
        RefreshPreview();
    }

    private void RefreshSwatches()
    {
        Swatches.Clear();
        foreach (var c in _include)
            Swatches.Add(new ColorSwatch(ToBrush(c), IncludedOutline, $"Selected #{c.R:X2}{c.G:X2}{c.B:X2}"));
        foreach (var c in _exclude)
            Swatches.Add(new ColorSwatch(ToBrush(c), ExcludedOutline, $"Excluded #{c.R:X2}{c.G:X2}{c.B:X2}"));
    }

    private static IBrush ToBrush(SampledColor c) => new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));

    /// <summary>Selection preview: white = selected, black = not. Image preview: thumbnail over a checkerboard.</summary>
    private void RefreshPreview()
    {
        if (_thumbnail is null) return;
        int w = _thumbnail.Width;
        int h = _thumbnail.Height;
        var coverage = ShowSelectionPreview ? ColorRange.Coverage(_thumbnail, Options) : null;
        var px = _thumbnail.PixelsReadOnly;

        var bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        var row = new int[w];
        using (var fb = bitmap.Lock())
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int r, g, b;
                    if (coverage is not null)
                    {
                        r = g = b = coverage[y * w + x];
                    }
                    else
                    {
                        int i = (y * w + x) * 4;
                        int a = px[i + 3];
                        int bg = ((x / 8 + y / 8) & 1) == 0 ? 0x3A : 0x55;
                        r = (px[i] * a + bg * (255 - a) + 127) / 255;
                        g = (px[i + 1] * a + bg * (255 - a) + 127) / 255;
                        b = (px[i + 2] * a + bg * (255 - a) + 127) / 255;
                    }
                    row[x] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, w);
            }
        }
        Preview = bitmap;
    }

    public void Dispose()
    {
        _thumbnail?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public sealed record SelectionModeOption(SelectionCombineMode Mode, string Label)
{
    public override string ToString() => Label;
}

public sealed record ColorSwatch(IBrush Fill, IBrush Outline, string Tip);
