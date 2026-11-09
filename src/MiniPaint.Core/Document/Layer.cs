using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Document;

public sealed class Layer : LayerNode
{
    private bool _disposed;

    /// <summary>Offset of the layer bitmap origin in document coordinates.</summary>
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }

    public PixelBuffer Pixels { get; private set; }

    /// <summary>
    /// Layer mask: one value per pixel of <see cref="Pixels"/> (row-major, same size and place),
    /// 255 shows the pixel, 0 hides it. Null when the layer has no mask.
    /// </summary>
    public byte[]? Mask { get; private set; }

    public bool HasMask => Mask is not null;

    public Layer(string name, int width, int height) : base(name)
    {
        Pixels = new PixelBuffer(width, height);
    }

    public Layer(string name, PixelBuffer pixels, int offsetX = 0, int offsetY = 0) : base(name)
    {
        Pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public Layer Clone(string? newName = null)
    {
        ThrowIfDisposed();
        var copy = new Layer(newName ?? Name + " copy", Pixels.Clone(), OffsetX, OffsetY);
        CopyPropertiesTo(copy);
        copy.Mask = (byte[]?)Mask?.Clone();
        return copy;
    }

    public override LayerNode CloneNode(string? newName = null) => Clone(newName);

    /// <summary>
    /// Replace the pixels. A mask follows a size change by resampling; callers that transform the
    /// mask themselves use <see cref="ReplacePixels(PixelBuffer, byte[])"/>.
    /// </summary>
    public void ReplacePixels(PixelBuffer pixels)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(pixels);
        if (Mask is not null && (pixels.Width != Pixels.Width || pixels.Height != Pixels.Height))
            Mask = LayerMask.Resample(Mask, Pixels.Width, Pixels.Height, pixels.Width, pixels.Height);
        var old = Pixels;
        Pixels = pixels;
        old.Dispose();
    }

    /// <summary>Replace pixels and mask together (null removes the mask). Takes ownership of both.</summary>
    public void ReplacePixels(PixelBuffer pixels, byte[]? mask)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(pixels);
        LayerMask.ThrowIfWrongSize(mask, pixels.Width, pixels.Height);
        var old = Pixels;
        Pixels = pixels;
        Mask = mask;
        old.Dispose();
    }

    /// <summary>Set (takes ownership) or remove (null) the mask; it must match the pixel size.</summary>
    public void SetMask(byte[]? mask)
    {
        ThrowIfDisposed();
        LayerMask.ThrowIfWrongSize(mask, Pixels.Width, Pixels.Height);
        Mask = mask;
    }

    public override void Dispose()
    {
        if (_disposed) return;
        Pixels.Dispose();
        Mask = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
