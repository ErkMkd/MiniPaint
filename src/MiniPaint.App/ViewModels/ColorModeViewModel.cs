using CommunityToolkit.Mvvm.ComponentModel;
using MiniPaint.Core.ColorMode;

namespace MiniPaint.App.ViewModels;

public partial class ColorModeViewModel : ViewModelBase
{
    private readonly DocumentColorSettings _original;
    private readonly Action<DocumentColorSettings>? _applyPreview;
    private bool _suspendPreview;

    public string Title => "Image Mode";

    public ColorModeOption[] Modes { get; } =
    [
        new(ColorModeKind.Rgba, "RGBA (truecolor)"),
        new(ColorModeKind.Indexed, "Indexed Color"),
        new(ColorModeKind.Ecs, "Amiga ECS (12-bit / 4096)"),
        new(ColorModeKind.Aga, "Amiga AGA (24-bit palette)")
    ];

    public QuantizerOption[] Quantizers { get; } =
    [
        new(QuantizerKind.MedianCut, "Median Cut"),
        new(QuantizerKind.Octree, "Octree"),
        new(QuantizerKind.Wu, "Wu"),
        new(QuantizerKind.KMeansPlusPlus, "K-Means++"),
        new(QuantizerKind.NeuQuant, "NeuQuant")
    ];

    public DitherOption[] Dithers { get; } =
    [
        new(DitherKind.None, "None"),
        new(DitherKind.FloydSteinberg, "Floyd–Steinberg"),
        new(DitherKind.Bayer4x4, "Bayer 4×4"),
        new(DitherKind.Bayer8x8, "Bayer 8×8"),
        new(DitherKind.Atkinson, "Atkinson"),
        new(DitherKind.Stucki, "Stucki"),
        new(DitherKind.BlueNoise, "Blue Noise")
    ];

    public int[] EcsColorCounts { get; } = [2, 4, 8, 16, 32, 64];
    public int[] AgaColorCounts { get; } = [2, 4, 8, 16, 32, 64, 128, 256];

    [ObservableProperty] public partial ColorModeOption SelectedMode { get; set; }
    [ObservableProperty] public partial QuantizerOption SelectedQuantizer { get; set; }
    [ObservableProperty] public partial DitherOption SelectedDither { get; set; }
    [ObservableProperty] public partial decimal ColorCountValue { get; set; } = 256;
    [ObservableProperty] public partial int SelectedEcsCount { get; set; } = 32;
    [ObservableProperty] public partial int SelectedAgaCount { get; set; } = 256;
    [ObservableProperty] public partial bool PreviewEnabled { get; set; }

    public bool ShowIndexedCount => SelectedMode.Kind == ColorModeKind.Indexed;
    public bool ShowEcsCount => SelectedMode.Kind == ColorModeKind.Ecs;
    public bool ShowAgaCount => SelectedMode.Kind == ColorModeKind.Aga;
    public bool ShowQuantizer => SelectedMode.Kind != ColorModeKind.Rgba;
    public bool ShowHalfBrightNote =>
        SelectedMode.Kind == ColorModeKind.Ecs && SelectedEcsCount == 64;

    public string Subtitle => SelectedMode.Kind switch
    {
        ColorModeKind.Rgba => "Full RGBA compositing — no palette constraint.",
        ColorModeKind.Indexed => "Modern indexed color: free color count (2–256), 24-bit palette.",
        ColorModeKind.Ecs => "Amiga ECS: powers of 2 up to 32, or 64 with Extra Half-Brite. Palette from 4096 colors.",
        ColorModeKind.Aga => "Amiga AGA: powers of 2 from 2–256. 24-bit palette entries.",
        _ => ""
    };

    /// <param name="applyPreview">Called with settings to show on the canvas (temporary). Pass original to restore.</param>
    public ColorModeViewModel(DocumentColorSettings current, Action<DocumentColorSettings>? applyPreview = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        current.Sanitize();
        _original = current.Clone();
        _applyPreview = applyPreview;

        _suspendPreview = true;
        SelectedMode = Modes.First(m => m.Kind == current.Mode);
        SelectedQuantizer = Quantizers.First(q => q.Kind == current.Quantizer);
        SelectedDither = Dithers.First(d => d.Kind == current.Dither);
        ColorCountValue = current.Mode == ColorModeKind.Indexed ? current.ColorCount : 256;
        SelectedEcsCount = current.Mode == ColorModeKind.Ecs ? current.ColorCount : 32;
        SelectedAgaCount = current.Mode == ColorModeKind.Aga ? current.ColorCount : 256;
        PreviewEnabled = false;
        _suspendPreview = false;
    }

    partial void OnSelectedModeChanged(ColorModeOption value)
    {
        RaiseVisibility();
        RequestPreviewRefresh();
    }

    partial void OnSelectedEcsCountChanged(int value)
    {
        OnPropertyChanged(nameof(ShowHalfBrightNote));
        RequestPreviewRefresh();
    }

    partial void OnSelectedAgaCountChanged(int value) => RequestPreviewRefresh();
    partial void OnColorCountValueChanged(decimal value) => RequestPreviewRefresh();
    partial void OnSelectedQuantizerChanged(QuantizerOption value) => RequestPreviewRefresh();
    partial void OnSelectedDitherChanged(DitherOption value) => RequestPreviewRefresh();
    partial void OnPreviewEnabledChanged(bool value) => RequestPreviewRefresh();

    private void RaiseVisibility()
    {
        OnPropertyChanged(nameof(ShowIndexedCount));
        OnPropertyChanged(nameof(ShowEcsCount));
        OnPropertyChanged(nameof(ShowAgaCount));
        OnPropertyChanged(nameof(ShowQuantizer));
        OnPropertyChanged(nameof(ShowHalfBrightNote));
        OnPropertyChanged(nameof(Subtitle));
    }

    public void RequestPreviewRefresh()
    {
        if (_suspendPreview || _applyPreview is null) return;

        if (!PreviewEnabled)
        {
            _applyPreview(_original);
            return;
        }

        _applyPreview(ToSettings(_original));
    }

    /// <summary>Restore the document to the settings from when the dialog opened.</summary>
    public void RestoreOriginal() => _applyPreview?.Invoke(_original);

    public DocumentColorSettings ToSettings(DocumentColorSettings? previous = null)
    {
        previous ??= _original;
        var s = new DocumentColorSettings
        {
            Mode = SelectedMode.Kind,
            Quantizer = SelectedQuantizer.Kind,
            Dither = SelectedDither.Kind,
            ColorCount = SelectedMode.Kind switch
            {
                ColorModeKind.Indexed => (int)Math.Clamp(ColorCountValue, 2, 256),
                ColorModeKind.Ecs => SelectedEcsCount,
                ColorModeKind.Aga => SelectedAgaCount,
                _ => 0
            },
            HalfBright = SelectedMode.Kind == ColorModeKind.Ecs && SelectedEcsCount == 64
        };
        s.Sanitize();

        // Keep existing palette when only dither (or identical mode/count/quantizer) changes.
        if (previous.Mode == s.Mode &&
            previous.ColorCount == s.ColorCount &&
            previous.Quantizer == s.Quantizer &&
            previous.HasValidPalette &&
            previous.Palette is not null)
        {
            s.SetPalette(previous.Palette, previous.PaletteCustomized);
        }

        return s;
    }
}

public sealed record ColorModeOption(ColorModeKind Kind, string Label)
{
    public override string ToString() => Label;
}

public sealed record QuantizerOption(QuantizerKind Kind, string Label)
{
    public override string ToString() => Label;
}

public sealed record DitherOption(DitherKind Kind, string Label)
{
    public override string ToString() => Label;
}
