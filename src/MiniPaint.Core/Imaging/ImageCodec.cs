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

    /// <summary>
    /// Save via a sibling temp file then replace, with short retries — avoids hanging/failing
    /// when the destination is briefly locked (Explorer, antivirus, Avalonia save picker).
    /// </summary>
    public static void SavePng(PixelBuffer buffer, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                SavePng(buffer, stream);

            ReplaceWithRetry(temp, path);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* ignore */ }
        }
    }

    public static void SavePng(PixelBuffer buffer, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(stream);

        var info = new SKImageInfo(buffer.Width, buffer.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var src = buffer.PixelsReadOnly;
        unsafe
        {
            fixed (byte* p = src)
            {
                Buffer.MemoryCopy(p, (void*)bitmap.GetPixels(), src.Length, src.Length);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encode failed.");
        data.SaveTo(stream);
        stream.Flush();
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
        unsafe
        {
            fixed (byte* p = src)
            {
                Buffer.MemoryCopy(p, (void*)bitmap.GetPixels(), src.Length, src.Length);
            }
        }
        return bitmap;
    }

    private static void ReplaceWithRetry(string tempPath, string destinationPath)
    {
        const int attempts = 15;
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                File.Copy(tempPath, destinationPath, overwrite: true);
                return;
            }
            catch (IOException) when (i < attempts - 1)
            {
                Thread.Sleep(40 * (i + 1));
            }
            catch (UnauthorizedAccessException) when (i < attempts - 1)
            {
                Thread.Sleep(40 * (i + 1));
            }
        }

        // Last attempt — let the exception propagate with context.
        File.Copy(tempPath, destinationPath, overwrite: true);
    }
}
