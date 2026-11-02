using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Clipboard;

/// <summary>
/// Internal RGBA clipboard. Optionally syncs PNG bytes for the system clipboard via callbacks
/// supplied by the UI layer (keeps Core free of Avalonia).
/// </summary>
public sealed class ImageClipboard
{
    public PixelBuffer? Pixels { get; private set; }
    public int OriginX { get; private set; }
    public int OriginY { get; private set; }
    public bool HasContent => Pixels is not null;

    /// <summary>Optional: write PNG bytes to the OS clipboard.</summary>
    public Action<byte[]>? WriteSystemPng { get; set; }

    /// <summary>Optional: read PNG bytes from the OS clipboard.</summary>
    public Func<byte[]?>? ReadSystemPng { get; set; }

    public void Set(PixelBuffer pixels, int originX, int originY)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        Pixels?.Dispose();
        Pixels = pixels;
        OriginX = originX;
        OriginY = originY;

        try
        {
            WriteSystemPng?.Invoke(ImageCodec.EncodePngBytes(pixels));
        }
        catch
        {
            // System clipboard is best-effort.
        }
    }

    public PixelBuffer? PeekClone()
    {
        TryImportFromSystem();
        return Pixels?.Clone();
    }

    /// <summary>
    /// Paste as a new transparent layer into the target document.
    /// </summary>
    public Layer? PasteAsNewLayer(PaintDocument document, string layerName, int? docX = null, int? docY = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        TryImportFromSystem();
        if (Pixels is null) return null;

        int x = docX ?? OriginX;
        int y = docY ?? OriginY;
        var layer = new Layer(layerName, Pixels.Clone(), x, y);
        document.AddLayer(layer);
        return layer;
    }

    private void TryImportFromSystem()
    {
        if (Pixels is not null || ReadSystemPng is null) return;
        try
        {
            var bytes = ReadSystemPng();
            if (bytes is null || bytes.Length == 0) return;
            var decoded = ImageCodec.DecodePngBytes(bytes);
            Pixels?.Dispose();
            Pixels = decoded;
            OriginX = 0;
            OriginY = 0;
        }
        catch
        {
            // Ignore corrupt / unsupported clipboard data.
        }
    }

    public void Clear()
    {
        Pixels?.Dispose();
        Pixels = null;
        OriginX = OriginY = 0;
    }
}
