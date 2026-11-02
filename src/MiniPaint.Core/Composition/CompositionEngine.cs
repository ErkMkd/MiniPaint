using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Composition;

/// <summary>
/// Composites visible layers (bottom → top) with correct alpha into a document-sized buffer.
/// </summary>
public static class CompositionEngine
{
    public static PixelBuffer Composite(PaintDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new PixelBuffer(document.Width, document.Height);
        // Transparent canvas by default.
        foreach (var layer in document.Layers)
        {
            if (!layer.IsVisible || layer.Opacity <= 0f)
                continue;

            PixelOps.BlendLayer(result, layer.Pixels, layer.OffsetX, layer.OffsetY, layer.Opacity);
        }

        return result;
    }

    /// <summary>
    /// Composite into an existing buffer (cleared first). Avoids allocating when callers reuse a cache.
    /// </summary>
    public static void CompositeInto(PaintDocument document, PixelBuffer destination)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Width != document.Width || destination.Height != document.Height)
            throw new ArgumentException("Destination size must match document.");

        destination.Clear(0, 0, 0, 0);
        foreach (var layer in document.Layers)
        {
            if (!layer.IsVisible || layer.Opacity <= 0f)
                continue;

            PixelOps.BlendLayer(destination, layer.Pixels, layer.OffsetX, layer.OffsetY, layer.Opacity);
        }
    }
}
