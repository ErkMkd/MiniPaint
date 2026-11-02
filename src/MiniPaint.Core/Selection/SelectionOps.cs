using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Selection;

public static class SelectionOps
{
    /// <summary>
    /// Copy pixels from the active layer within the selection (transparent outside the layer).
    /// </summary>
    public static PixelBuffer? CopyFromActiveLayer(PaintDocument document, SelectionState selection)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selection);
        if (!selection.HasSelection) return null;

        var layer = document.ActiveLayer;
        if (layer is null) return null;

        return PixelOps.ExtractRegion(
            layer.Pixels,
            layer.OffsetX,
            layer.OffsetY,
            selection.X,
            selection.Y,
            selection.Width,
            selection.Height);
    }

    /// <summary>
    /// Cut: copy then clear the selection on the active layer.
    /// </summary>
    public static PixelBuffer? CutFromActiveLayer(PaintDocument document, SelectionState selection)
    {
        var copied = CopyFromActiveLayer(document, selection);
        if (copied is null) return null;

        var layer = document.ActiveLayer!;
        PixelOps.ClearRegion(
            layer.Pixels,
            layer.OffsetX,
            layer.OffsetY,
            selection.X,
            selection.Y,
            selection.Width,
            selection.Height);
        document.IsDirty = true;
        return copied;
    }
}
