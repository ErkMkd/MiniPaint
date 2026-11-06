using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MiniPaint.Core.Document;

namespace MiniPaint.App.ViewModels;

public partial class ResizeAllViewModel : ViewModelBase
{
    private readonly IReadOnlyList<ResizeAllDocument> _documents;

    public ResizeModeOption[] Modes { get; } =
    [
        new(BatchResizeMode.Percentage, "Percentage"),
        new(BatchResizeMode.FitWithin, "Fit within (keep proportions)"),
        new(BatchResizeMode.ExactSize, "Exact size (stretch)")
    ];

    [ObservableProperty] public partial ResizeModeOption SelectedMode { get; set; }
    [ObservableProperty] public partial decimal PercentValue { get; set; }
    [ObservableProperty] public partial decimal WidthValue { get; set; }
    [ObservableProperty] public partial decimal HeightValue { get; set; }
    [ObservableProperty] public partial bool DoNotEnlarge { get; set; }

    /// <summary>Before → after size of every open image for the current settings.</summary>
    public ObservableCollection<ResizeAllPreviewItem> Preview { get; } = new();

    public string Title => "Resize All Images";

    public string Subtitle =>
        $"Scale the pixels of all {_documents.Count} open images. Each image can be undone on its own tab (Ctrl+Z).";

    public string InterpolationNote =>
        $"Interpolation: {DocumentResize.ImageInterpolationName}.";

    public bool ShowPercent => SelectedMode.Mode == BatchResizeMode.Percentage;
    public bool ShowBox => SelectedMode.Mode != BatchResizeMode.Percentage;
    public bool ShowDoNotEnlarge => SelectedMode.Mode == BatchResizeMode.FitWithin;

    public string ModeNote => SelectedMode.Mode switch
    {
        BatchResizeMode.Percentage => "Every image is scaled by the same percentage.",
        BatchResizeMode.FitWithin => "Each image is scaled to the largest size that fits inside the box; proportions are kept.",
        BatchResizeMode.ExactSize => "Every image gets exactly this size; images with other proportions are stretched.",
        _ => ""
    };

    public BatchResizeOptions Options => new(
        SelectedMode.Mode,
        (double)PercentValue,
        (int)Math.Round(WidthValue),
        (int)Math.Round(HeightValue),
        DoNotEnlarge);

    public ResizeAllViewModel() : this([new ResizeAllDocument("Untitled", 800, 600)], null, 800, 600)
    {
    }

    /// <param name="last">Settings from the previous Resize All this session, if any.</param>
    /// <param name="boxWidth">Initial box width when there are no previous settings.</param>
    /// <param name="boxHeight">Initial box height when there are no previous settings.</param>
    public ResizeAllViewModel(
        IReadOnlyList<ResizeAllDocument> documents,
        BatchResizeOptions? last,
        int boxWidth,
        int boxHeight)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;

        var o = last ?? new BatchResizeOptions(BatchResizeMode.Percentage, 50, boxWidth, boxHeight, DoNotEnlarge: true);
        SelectedMode = Modes.First(m => m.Mode == o.Mode);
        PercentValue = (decimal)o.Percent;
        WidthValue = Math.Max(1, o.Width);
        HeightValue = Math.Max(1, o.Height);
        DoNotEnlarge = o.DoNotEnlarge;
        RefreshPreview();
    }

    partial void OnSelectedModeChanged(ResizeModeOption value)
    {
        OnPropertyChanged(nameof(ShowPercent));
        OnPropertyChanged(nameof(ShowBox));
        OnPropertyChanged(nameof(ShowDoNotEnlarge));
        OnPropertyChanged(nameof(ModeNote));
        RefreshPreview();
    }

    partial void OnPercentValueChanged(decimal value) => RefreshPreview();
    partial void OnWidthValueChanged(decimal value) => RefreshPreview();
    partial void OnHeightValueChanged(decimal value) => RefreshPreview();
    partial void OnDoNotEnlargeChanged(bool value) => RefreshPreview();

    public bool TryCommit()
    {
        try
        {
            foreach (var d in _documents)
                BatchResize.ComputeTargetSize(d.Width, d.Height, Options);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private void RefreshPreview()
    {
        Preview.Clear();
        foreach (var d in _documents)
        {
            string change;
            try
            {
                var (w, h) = BatchResize.ComputeTargetSize(d.Width, d.Height, Options);
                change = w == d.Width && h == d.Height
                    ? $"{d.Width} × {d.Height} (unchanged)"
                    : $"{d.Width} × {d.Height}  →  {w} × {h}";
            }
            catch (ArgumentOutOfRangeException)
            {
                change = "—";
            }
            Preview.Add(new ResizeAllPreviewItem(d.Name, change));
        }
    }
}

public readonly record struct ResizeAllDocument(string Name, int Width, int Height);

public sealed record ResizeAllPreviewItem(string Name, string Change);

public sealed record ResizeModeOption(BatchResizeMode Mode, string Label)
{
    public override string ToString() => Label;
}
