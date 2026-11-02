using SkiaSharp;

namespace MiniPaint.Core.Imaging;

/// <summary>
/// Load/save raster images via SkiaSharp. Converts to/from RGBA8888 PixelBuffer.
/// </summary>
public static class ImageCodec
{
    public static PixelBuffer Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public static PixelBuffer Load(Stream stream)
    {
        using var codec = SKCodec.Create(stream)
            ?? throw new InvalidOperationException("Unsupported or corrupt image.");

        var info = codec.Info;
        var desired = new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var pixels = new byte[desired.BytesSize];

        var result = codec.GetPixels(desired, pixels);
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            throw new InvalidOperationException($"Failed to decode image: {result}");

        return new PixelBuffer(desired.Width, desired.Height, pixels);
    }

    public static void SavePng(PixelBuffer buffer, string path)
    {
        using var stream = File.Create(path);
        SavePng(buffer, stream);
    }

    public static void SavePng(PixelBuffer buffer, Stream stream)
    {
        var info = new SKImageInfo(buffer.Width, buffer.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var span = buffer.PixelsReadOnly;
        System.Runtime.InteropServices.Marshal.Copy(
            span.ToArray(), 0, bitmap.GetPixels(), span.Length);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encode failed.");
        data.SaveTo(stream);
    }

    public static byte[] EncodePngBytes(PixelBuffer buffer)
    {
        using var ms = new MemoryStream();
        SavePng(buffer, ms);
        return ms.ToArray();
    }

    public static PixelBuffer DecodePngBytes(byte[] data)
    {
        using var ms = new MemoryStream(data);
        return Load(ms);
    }

    /// <summary>
    /// Create an SKBitmap view copy for UI rendering. Caller owns the bitmap.
    /// </summary>
    public static SKBitmap ToSkBitmap(PixelBuffer buffer)
    {
        var info = new SKImageInfo(buffer.Width, buffer.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        var src = buffer.PixelsReadOnly;
        System.Runtime.InteropServices.Marshal.Copy(
            src.ToArray(), 0, bitmap.GetPixels(), src.Length);
        return bitmap;
    }
}
