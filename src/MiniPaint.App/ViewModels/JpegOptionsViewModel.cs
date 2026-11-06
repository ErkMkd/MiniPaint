using CommunityToolkit.Mvvm.ComponentModel;
using MiniPaint.Core.Imaging;

namespace MiniPaint.App.ViewModels;

/// <summary>
/// JPEG quality (1–100), shared by File → Export JPG and File → Export All Images.
/// </summary>
public partial class JpegOptionsViewModel : ViewModelBase
{
    [ObservableProperty] public partial decimal QualityValue { get; set; }

    public JpegOptionsViewModel() : this(ImageCodec.DefaultJpegQuality)
    {
    }

    public JpegOptionsViewModel(int quality)
    {
        QualityValue = Math.Clamp(quality, 1, 100);
    }

    public string Title => "JPG Options";

    public int Quality => (int)Math.Clamp(Math.Round(QualityValue), 1, 100);

    /// <summary>Slider mirror of <see cref="QualityValue"/> (Slider is double, NumericUpDown decimal).</summary>
    public double QualitySliderValue
    {
        get => (double)QualityValue;
        set => QualityValue = Math.Clamp(Math.Round((decimal)value), 1, 100);
    }

    public string QualityHint => Quality switch
    {
        >= 90 => "High quality — larger file.",
        >= 70 => "Good quality — balanced file size.",
        >= 40 => "Medium quality — compression artifacts may show.",
        _ => "Low quality — strongest compression, smallest file."
    };

    partial void OnQualityValueChanged(decimal value)
    {
        OnPropertyChanged(nameof(Quality));
        OnPropertyChanged(nameof(QualitySliderValue));
        OnPropertyChanged(nameof(QualityHint));
    }
}
