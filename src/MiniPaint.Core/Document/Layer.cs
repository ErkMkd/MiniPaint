using MiniPaint.Core.Composition;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Document;

public sealed class Layer : IDisposable
{
    private bool _disposed;

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }
    public bool IsVisible { get; set; } = true;
    public float Opacity { get; set; } = 1f;
    public BlendMode BlendMode { get; set; } = BlendMode.Normal;

    /// <summary>Offset of the layer bitmap origin in document coordinates.</summary>
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }

    public PixelBuffer Pixels { get; private set; }

    public Layer(string name, int width, int height)
    {
        Name = name;
        Pixels = new PixelBuffer(width, height);
    }

    public Layer(string name, PixelBuffer pixels, int offsetX = 0, int offsetY = 0)
    {
        Name = name;
        Pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public Layer Clone(string? newName = null)
    {
        ThrowIfDisposed();
        return new Layer(newName ?? Name + " copy", Pixels.Clone(), OffsetX, OffsetY)
        {
            IsVisible = IsVisible,
            Opacity = Opacity,
            BlendMode = BlendMode
        };
    }

    public void ReplacePixels(PixelBuffer pixels)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(pixels);
        var old = Pixels;
        Pixels = pixels;
        old.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Pixels.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
