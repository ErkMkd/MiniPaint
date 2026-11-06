namespace MiniPaint.Core.Document;

public enum BatchResizeMode
{
    /// <summary>Scale width and height by <see cref="BatchResizeOptions.Percent"/>.</summary>
    Percentage,

    /// <summary>Largest size that fits inside Width × Height, keeping proportions.</summary>
    FitWithin,

    /// <summary>Exactly Width × Height; proportions are not kept.</summary>
    ExactSize
}

/// <summary>One sizing rule applied to every open document (Image → Resize All Images).</summary>
public readonly record struct BatchResizeOptions(
    BatchResizeMode Mode,
    double Percent = 100,
    int Width = 0,
    int Height = 0,
    bool DoNotEnlarge = false);

public static class BatchResize
{
    public const int MaxSize = 16384;

    /// <summary>
    /// Target pixel size for a <paramref name="width"/> × <paramref name="height"/> document.
    /// <see cref="BatchResizeOptions.DoNotEnlarge"/> only applies to <see cref="BatchResizeMode.FitWithin"/>.
    /// </summary>
    public static (int Width, int Height) ComputeTargetSize(int width, int height, BatchResizeOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        switch (options.Mode)
        {
            case BatchResizeMode.Percentage:
            {
                if (!(options.Percent > 0))
                    throw new ArgumentOutOfRangeException(nameof(options), "Percent must be positive.");
                double s = options.Percent / 100.0;
                return (ClampSize(width * s), ClampSize(height * s));
            }
            case BatchResizeMode.FitWithin:
            {
                ThrowIfBoxInvalid(options);
                double s = Math.Min(options.Width / (double)width, options.Height / (double)height);
                if (options.DoNotEnlarge)
                    s = Math.Min(s, 1.0);
                return (ClampSize(width * s), ClampSize(height * s));
            }
            case BatchResizeMode.ExactSize:
                ThrowIfBoxInvalid(options);
                return (ClampSize(options.Width), ClampSize(options.Height));
            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Mode, "Unknown resize mode.");
        }
    }

    private static void ThrowIfBoxInvalid(BatchResizeOptions options)
    {
        if (options.Width <= 0 || options.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Width and height must be positive.");
    }

    private static int ClampSize(double value) => (int)Math.Clamp(Math.Round(value), 1, MaxSize);
}
