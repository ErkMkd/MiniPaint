using System.Runtime.CompilerServices;

namespace MiniPaint.Core.Imaging;

/// <summary>
/// Contiguous RGBA8888 pixel buffer. Owns its memory; callers must Dispose.
/// </summary>
public sealed class PixelBuffer : IDisposable
{
    private byte[]? _pixels;
    private bool _disposed;

    public int Width { get; }
    public int Height { get; }
    public int Stride => Width * 4;
    public int Length => Width * Height * 4;

    public PixelBuffer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _pixels = new byte[Length];
    }

    public PixelBuffer(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length < width * height * 4)
            throw new ArgumentException("Pixel array is too small.", nameof(pixels));

        Width = width;
        Height = height;
        _pixels = pixels;
    }

    public Span<byte> Pixels
    {
        get
        {
            ThrowIfDisposed();
            return _pixels!.AsSpan(0, Length);
        }
    }

    public ReadOnlySpan<byte> PixelsReadOnly
    {
        get
        {
            ThrowIfDisposed();
            return _pixels!.AsSpan(0, Length);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Offset(int x, int y) => (y * Width + x) * 4;

    public PixelBuffer Clone()
    {
        ThrowIfDisposed();
        var copy = new byte[Length];
        Buffer.BlockCopy(_pixels!, 0, copy, 0, Length);
        return new PixelBuffer(Width, Height, copy);
    }

    /// <summary>
    /// Copy all rows into unmanaged memory (e.g. a locked bitmap) whose rows are
    /// <paramref name="destinationRowBytes"/> apart — no intermediate managed copy.
    /// </summary>
    public unsafe void CopyTo(IntPtr destination, int destinationRowBytes)
    {
        ThrowIfDisposed();
        if (destinationRowBytes < Stride)
            throw new ArgumentOutOfRangeException(nameof(destinationRowBytes));

        fixed (byte* src = _pixels)
        {
            var dst = (byte*)destination;
            if (destinationRowBytes == Stride)
            {
                Buffer.MemoryCopy(src, dst, Length, Length);
                return;
            }
            for (int y = 0; y < Height; y++)
                Buffer.MemoryCopy(src + y * Stride, dst + (long)y * destinationRowBytes, destinationRowBytes, Stride);
        }
    }

    public void Clear(byte r = 0, byte g = 0, byte b = 0, byte a = 0)
    {
        ThrowIfDisposed();
        var px = _pixels!;
        if ((r | g | b | a) == 0)
        {
            px.AsSpan(0, Length).Clear();
            return;
        }
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = r;
            px[i + 1] = g;
            px[i + 2] = b;
            px[i + 3] = a;
        }
    }

    public void FillRect(int x, int y, int w, int h, byte r, byte g, byte b, byte a)
    {
        ThrowIfDisposed();
        int x0 = Math.Max(0, x);
        int y0 = Math.Max(0, y);
        int x1 = Math.Min(Width, x + w);
        int y1 = Math.Min(Height, y + h);
        var px = _pixels!;
        for (int yy = y0; yy < y1; yy++)
        {
            int row = yy * Width * 4;
            for (int xx = x0; xx < x1; xx++)
            {
                int i = row + xx * 4;
                px[i] = r;
                px[i + 1] = g;
                px[i + 2] = b;
                px[i + 3] = a;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _pixels = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed || _pixels is null, this);
    }
}
