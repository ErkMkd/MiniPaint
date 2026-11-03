using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniPaint.Core.ColorMode;
using MiniPaint.Core.Imaging;

namespace MiniPaint.App.ViewModels;

public partial class PaletteEditorViewModel : ViewModelBase
{
    private readonly DocumentColorSettings _settings;
    private readonly PixelBuffer _compositeSource;
    private readonly List<PaletteRgb> _working;
    private bool _syncing;

    public string Title => "Palette";
    public bool IsEcs => _settings.Mode == ColorModeKind.Ecs;
    public bool IsEhb => IsEcs && _settings.HalfBright;
    public int BitsPerChannel => _settings.BitsPerChannel;
    public int EditableCount => _working.Count;

    public string Subtitle => _settings.Mode switch
    {
        ColorModeKind.Ecs when IsEhb =>
            $"ECS Extra Half-Brite — {EditableCount} editable colors from the 4096-color (12-bit) space. Indices {EditableCount}–{EditableCount * 2 - 1} are automatic half-brite.",
        ColorModeKind.Ecs =>
            $"ECS — {EditableCount} colors chosen from the 4096-color space (16 levels × R/G/B).",
        ColorModeKind.Aga =>
            $"AGA — {EditableCount} colors, 24-bit palette entries (256 levels × R/G/B).",
        ColorModeKind.Indexed =>
            $"Indexed — {EditableCount} colors, 24-bit palette.",
        _ => ""
    };

    public ObservableCollection<PaletteSwatchItem> Swatches { get; } = new();

    [ObservableProperty] public partial PaletteSwatchItem? SelectedSwatch { get; set; }

    // ECS: edit as nibbles 0–15 (4096 space). AGA/Indexed: 0–255.
    [ObservableProperty] public partial decimal ChannelR { get; set; }
    [ObservableProperty] public partial decimal ChannelG { get; set; }
    [ObservableProperty] public partial decimal ChannelB { get; set; }

    public decimal ChannelMax => IsEcs ? 15 : 255;
    public string ChannelLabel => IsEcs ? "ECS components (0–15, 4096 colors)" : "RGB (0–255)";
    public bool CanEditSelected => SelectedSwatch is { IsEditable: true };

    public IBrush PreviewBrush =>
        new SolidColorBrush(Color.FromRgb(
            CurrentColor.R, CurrentColor.G, CurrentColor.B));

    public string HexLabel => $"#{CurrentColor.R:X2}{CurrentColor.G:X2}{CurrentColor.B:X2}";

    private PaletteRgb CurrentColor
    {
        get
        {
            if (IsEcs)
                return AmigaColorSpace.FromNibbles((int)ChannelR, (int)ChannelG, (int)ChannelB);
            return PaletteRgb.FromRgb((int)ChannelR, (int)ChannelG, (int)ChannelB);
        }
    }

    public PaletteEditorViewModel(DocumentColorSettings settings, PixelBuffer compositeSource)
    {
        _settings = settings.Clone();
        _settings.Sanitize();
        _compositeSource = compositeSource;

        var unique = ColorModePipeline.EnsureUniquePalette(compositeSource, _settings);
        _working = unique.ToList();
        RebuildSwatches();
        if (Swatches.Count > 0)
            SelectedSwatch = Swatches[0];
    }

    public DocumentColorSettings ResultSettings
    {
        get
        {
            var result = _settings.Clone();
            result.SetPalette(_working, customized: true);
            return result;
        }
    }

    partial void OnSelectedSwatchChanged(PaletteSwatchItem? value)
    {
        LoadChannelsFromSelection();
        OnPropertyChanged(nameof(CanEditSelected));
    }

    partial void OnChannelRChanged(decimal value) => PushChannelsToSelection();
    partial void OnChannelGChanged(decimal value) => PushChannelsToSelection();
    partial void OnChannelBChanged(decimal value) => PushChannelsToSelection();

    private void LoadChannelsFromSelection()
    {
        if (SelectedSwatch is null) return;
        _syncing = true;
        try
        {
            var c = _working[SelectedSwatch.Index];
            if (IsEcs)
            {
                var (r, g, b) = AmigaColorSpace.ToNibbles(c);
                ChannelR = r;
                ChannelG = g;
                ChannelB = b;
            }
            else
            {
                ChannelR = c.R;
                ChannelG = c.G;
                ChannelB = c.B;
            }
            RaisePreview();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void PushChannelsToSelection()
    {
        if (_syncing || SelectedSwatch is not { IsEditable: true }) return;
        int i = SelectedSwatch.Index;
        var color = IsEcs
            ? AmigaColorSpace.FromNibbles((int)ChannelR, (int)ChannelG, (int)ChannelB)
            : AmigaColorSpace.SnapColor(
                (byte)Math.Clamp((int)ChannelR, 0, 255),
                (byte)Math.Clamp((int)ChannelG, 0, 255),
                (byte)Math.Clamp((int)ChannelB, 0, 255),
                BitsPerChannel);

        _working[i] = color;
        SelectedSwatch.UpdateColor(color);
        if (IsEhb)
            RefreshHalfBriteMirrors(i);
        RaisePreview();
    }

    private void RefreshHalfBriteMirrors(int baseIndex)
    {
        // Display-only swatches after editable block.
        int mirrorIndex = EditableCount + baseIndex;
        foreach (var s in Swatches)
        {
            if (s.Index == mirrorIndex || (s.IsEditable && s.Index == baseIndex))
            {
                // mirror swatches use Index >= EditableCount with BaseIndex
            }
        }

        foreach (var s in Swatches.Where(x => !x.IsEditable && x.BaseIndex == baseIndex))
            s.UpdateColor(AmigaColorSpace.HalfBright(_working[baseIndex]));
    }

    private void RaisePreview()
    {
        OnPropertyChanged(nameof(PreviewBrush));
        OnPropertyChanged(nameof(HexLabel));
    }

    private void RebuildSwatches()
    {
        Swatches.Clear();
        for (int i = 0; i < _working.Count; i++)
            Swatches.Add(new PaletteSwatchItem(i, _working[i], isEditable: true, baseIndex: i));

        if (IsEhb)
        {
            for (int i = 0; i < _working.Count; i++)
            {
                Swatches.Add(new PaletteSwatchItem(
                    index: _working.Count + i,
                    color: AmigaColorSpace.HalfBright(_working[i]),
                    isEditable: false,
                    baseIndex: i));
            }
        }
    }

    [RelayCommand]
    private void RebuildFromImage()
    {
        var rebuilt = ColorModePipeline.EnsureUniquePalette(_compositeSource, _settings, forceRebuild: true);
        _working.Clear();
        _working.AddRange(rebuilt);
        var keep = SelectedSwatch?.Index ?? 0;
        RebuildSwatches();
        SelectedSwatch = Swatches.FirstOrDefault(s => s.Index == keep && s.IsEditable)
                         ?? Swatches.FirstOrDefault(s => s.IsEditable);
    }
}

public partial class PaletteSwatchItem : ObservableObject
{
    public int Index { get; }
    public int BaseIndex { get; }
    public bool IsEditable { get; }
    public string Caption => IsEditable ? $"{Index}" : $"{Index} (½)";

    [ObservableProperty] public partial IBrush Fill { get; set; }
    [ObservableProperty] public partial string Tip { get; set; }

    public PaletteSwatchItem(int index, PaletteRgb color, bool isEditable, int baseIndex)
    {
        Index = index;
        BaseIndex = baseIndex;
        IsEditable = isEditable;
        Fill = BrushFrom(color);
        Tip = FormatTip(color);
    }

    public void UpdateColor(PaletteRgb color)
    {
        Fill = BrushFrom(color);
        Tip = FormatTip(color);
    }

    private string FormatTip(PaletteRgb color)
    {
        if (!IsEditable)
            return $"#{Index} half-brite of #{BaseIndex} — #{color.R:X2}{color.G:X2}{color.B:X2}";
        var (nr, ng, nb) = AmigaColorSpace.ToNibbles(color);
        return $"#{Index}  #{color.R:X2}{color.G:X2}{color.B:X2}  · 12-bit {nr},{ng},{nb}";
    }

    private static IBrush BrushFrom(PaletteRgb c) =>
        new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
}
