namespace MiniPaint.Core.Composition;

/// <summary>
/// Photoshop-style layer blend modes (separable + HSL non-separable).
/// </summary>
public enum BlendMode
{
    Normal = 0,
    Dissolve, // approximated as Normal (stochastic dissolve skipped)
    Darken,
    Multiply,
    ColorBurn,
    LinearBurn,
    Lighten,
    Screen,
    ColorDodge,
    LinearDodge, // Add
    Overlay,
    SoftLight,
    HardLight,
    VividLight,
    LinearLight,
    PinLight,
    HardMix,
    Difference,
    Exclusion,
    Hue,
    Saturation,
    Color,
    Luminosity
}

public sealed record BlendModeOption(BlendMode Mode, string Label)
{
    public override string ToString() => Label;
}

public static class BlendModeNames
{
    public static readonly BlendModeOption[] All =
    [
        new(BlendMode.Normal, "Normal"),
        new(BlendMode.Dissolve, "Dissolve"),
        new(BlendMode.Darken, "Darken"),
        new(BlendMode.Multiply, "Multiply"),
        new(BlendMode.ColorBurn, "Color Burn"),
        new(BlendMode.LinearBurn, "Linear Burn"),
        new(BlendMode.Lighten, "Lighten"),
        new(BlendMode.Screen, "Screen"),
        new(BlendMode.ColorDodge, "Color Dodge"),
        new(BlendMode.LinearDodge, "Linear Dodge (Add)"),
        new(BlendMode.Overlay, "Overlay"),
        new(BlendMode.SoftLight, "Soft Light"),
        new(BlendMode.HardLight, "Hard Light"),
        new(BlendMode.VividLight, "Vivid Light"),
        new(BlendMode.LinearLight, "Linear Light"),
        new(BlendMode.PinLight, "Pin Light"),
        new(BlendMode.HardMix, "Hard Mix"),
        new(BlendMode.Difference, "Difference"),
        new(BlendMode.Exclusion, "Exclusion"),
        new(BlendMode.Hue, "Hue"),
        new(BlendMode.Saturation, "Saturation"),
        new(BlendMode.Color, "Color"),
        new(BlendMode.Luminosity, "Luminosity"),
    ];

    public static string ToLabel(BlendMode mode) =>
        All.FirstOrDefault(x => x.Mode == mode)?.Label ?? mode.ToString();

    public static BlendModeOption GetOption(BlendMode mode) =>
        All.FirstOrDefault(x => x.Mode == mode) ?? All[0];

    public static bool TryParse(string? text, out BlendMode mode)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            mode = BlendMode.Normal;
            return false;
        }

        if (Enum.TryParse(text, ignoreCase: true, out mode))
            return true;

        foreach (var opt in All)
        {
            if (string.Equals(opt.Label, text, StringComparison.OrdinalIgnoreCase))
            {
                mode = opt.Mode;
                return true;
            }
        }

        mode = BlendMode.Normal;
        return false;
    }
}
