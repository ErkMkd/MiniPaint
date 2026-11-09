using CommunityToolkit.Mvvm.ComponentModel;
using MiniPaint.Core.Filters;

namespace MiniPaint.App.ViewModels;

public enum ToneRange
{
    Shadows,
    Midtones,
    Highlights
}

/// <summary>
/// Settings of one filter (Brightness/Contrast, Hue/Saturation or Color Balance) while its dialog
/// is open. <see cref="Changed"/> fires on every edit so the canvas can preview <see cref="Result"/>.
/// </summary>
public partial class FilterDialogViewModel : ViewModelBase
{
    private readonly LayerFilter _initial;
    private readonly ToneShift[] _tones = new ToneShift[3];

    [ObservableProperty] public partial double Brightness { get; set; }
    [ObservableProperty] public partial double Contrast { get; set; }
    [ObservableProperty] public partial double Hue { get; set; }
    [ObservableProperty] public partial double Saturation { get; set; }
    [ObservableProperty] public partial double Value { get; set; }
    [ObservableProperty] public partial ToneRange Tone { get; set; } = ToneRange.Midtones;
    [ObservableProperty] public partial bool PreserveLuminosity { get; set; } = true;

    /// <summary>Show the filter on the canvas while editing (off = the image without it).</summary>
    [ObservableProperty] public partial bool Preview { get; set; } = true;

    public FilterDialogViewModel(LayerFilter initial, string targetName)
    {
        _initial = initial ?? throw new ArgumentNullException(nameof(initial));
        Title = $"{initial.Name} — {targetName}";
        Load(initial);
    }

    public event Action? Changed;

    public string Title { get; }

    public bool IsBrightnessContrast => _initial is BrightnessContrastFilter;
    public bool IsHueSaturation => _initial is HueSaturationFilter;
    public bool IsColorBalance => _initial is ColorBalanceFilter;

    // Color Balance: the three sliders show the selected tonal range.

    public double CyanRed
    {
        get => _tones[(int)Tone].CyanRed;
        set => SetTone(_tones[(int)Tone] with { CyanRed = (int)Math.Round(value) });
    }

    public double MagentaGreen
    {
        get => _tones[(int)Tone].MagentaGreen;
        set => SetTone(_tones[(int)Tone] with { MagentaGreen = (int)Math.Round(value) });
    }

    public double YellowBlue
    {
        get => _tones[(int)Tone].YellowBlue;
        set => SetTone(_tones[(int)Tone] with { YellowBlue = (int)Math.Round(value) });
    }

    public bool IsShadows
    {
        get => Tone == ToneRange.Shadows;
        set { if (value) Tone = ToneRange.Shadows; }
    }

    public bool IsMidtones
    {
        get => Tone == ToneRange.Midtones;
        set { if (value) Tone = ToneRange.Midtones; }
    }

    public bool IsHighlights
    {
        get => Tone == ToneRange.Highlights;
        set { if (value) Tone = ToneRange.Highlights; }
    }

    /// <summary>The filter with the current settings (it stays enabled or disabled as it was).</summary>
    public LayerFilter Result => _initial switch
    {
        BrightnessContrastFilter f => f with { Brightness = Round(Brightness), Contrast = Round(Contrast) },
        HueSaturationFilter f => f with { Hue = Round(Hue), Saturation = Round(Saturation), Value = Round(Value) },
        ColorBalanceFilter f => f with
        {
            Shadows = _tones[0],
            Midtones = _tones[1],
            Highlights = _tones[2],
            PreserveLuminosity = PreserveLuminosity
        },
        _ => _initial
    };

    /// <summary>Back to "no change" (the filter's neutral settings).</summary>
    public void Reset()
    {
        Load(_initial switch
        {
            BrightnessContrastFilter f => f with { Brightness = 0, Contrast = 0 },
            HueSaturationFilter f => f with { Hue = 0, Saturation = 0, Value = 0 },
            ColorBalanceFilter f => f with { Shadows = default, Midtones = default, Highlights = default, PreserveLuminosity = true },
            _ => _initial
        });
    }

    private void Load(LayerFilter filter)
    {
        switch (filter)
        {
            case BrightnessContrastFilter f:
                Brightness = f.Brightness;
                Contrast = f.Contrast;
                break;
            case HueSaturationFilter f:
                Hue = f.Hue;
                Saturation = f.Saturation;
                Value = f.Value;
                break;
            case ColorBalanceFilter f:
                _tones[0] = f.Shadows;
                _tones[1] = f.Midtones;
                _tones[2] = f.Highlights;
                PreserveLuminosity = f.PreserveLuminosity;
                RaiseToneSliders();
                Changed?.Invoke();
                break;
        }
    }

    private void SetTone(ToneShift shift)
    {
        if (_tones[(int)Tone] == shift) return;
        _tones[(int)Tone] = shift;
        RaiseToneSliders();
        Changed?.Invoke();
    }

    private void RaiseToneSliders()
    {
        OnPropertyChanged(nameof(CyanRed));
        OnPropertyChanged(nameof(MagentaGreen));
        OnPropertyChanged(nameof(YellowBlue));
    }

    partial void OnBrightnessChanged(double value) => Changed?.Invoke();
    partial void OnContrastChanged(double value) => Changed?.Invoke();
    partial void OnHueChanged(double value) => Changed?.Invoke();
    partial void OnSaturationChanged(double value) => Changed?.Invoke();
    partial void OnValueChanged(double value) => Changed?.Invoke();
    partial void OnPreserveLuminosityChanged(bool value) => Changed?.Invoke();
    partial void OnPreviewChanged(bool value) => Changed?.Invoke();

    partial void OnToneChanged(ToneRange value)
    {
        OnPropertyChanged(nameof(IsShadows));
        OnPropertyChanged(nameof(IsMidtones));
        OnPropertyChanged(nameof(IsHighlights));
        RaiseToneSliders();
    }

    private static int Round(double value) => (int)Math.Round(value);
}
