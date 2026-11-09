using MiniPaint.Core.Document;
using MiniPaint.Core.Filters;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Composition;

/// <summary>
/// Composites the visible layer stack (bottom → top) into a document-sized buffer: opacity, blend
/// modes, layer masks and filters. A group is composed on its own (isolated), then its filters,
/// opacity and blend mode apply to that result.
/// </summary>
public static class CompositionEngine
{
    public static PixelBuffer Composite(PaintDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new PixelBuffer(document.Width, document.Height);
        CompositeInto(document, result);
        return result;
    }

    public static void CompositeInto(PaintDocument document, PixelBuffer destination)
    {
        ArgumentNullException.ThrowIfNull(document);
        Compose(document, destination, stopAt: null);
    }

    /// <summary>
    /// What shows at the level of <paramref name="top"/>: the stack drawn up to and including it
    /// (everything above it in the stack is left out; the groups around it still apply).
    /// </summary>
    public static PixelBuffer CompositeThrough(PaintDocument document, LayerNode top)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(top);
        if (!document.Root.Contains(top) || ReferenceEquals(top, document.Root))
            throw new ArgumentException("The layer is not in the document.", nameof(top));
        var result = new PixelBuffer(document.Width, document.Height);
        Compose(document, result, top);
        return result;
    }

    private static void Compose(PaintDocument document, PixelBuffer destination, LayerNode? stopAt)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Width != document.Width || destination.Height != document.Height)
            throw new ArgumentException("Destination size must match document.");

        destination.Clear(0, 0, 0, 0);
        DrawChildren(document.Root, destination, stopAt);
    }

    /// <summary>Draw a group's children in order; true once <paramref name="stopAt"/> has been drawn.</summary>
    private static bool DrawChildren(LayerGroup group, PixelBuffer destination, LayerNode? stopAt)
    {
        foreach (var child in group.Children)
        {
            if (DrawNode(child, destination, stopAt))
                return true;
        }
        return false;
    }

    private static bool DrawNode(LayerNode node, PixelBuffer destination, LayerNode? stopAt)
    {
        if (!node.IsVisible || node.Opacity <= 0f)
            return stopAt is not null && node.Contains(stopAt);

        if (node is Layer layer)
        {
            DrawLayer(layer, destination);
            return ReferenceEquals(layer, stopAt);
        }

        var group = (LayerGroup)node;
        if (CanDrawInPlace(group))
            return DrawChildren(group, destination, stopAt) || ReferenceEquals(group, stopAt);

        using var content = new PixelBuffer(destination.Width, destination.Height);
        bool reached = DrawChildren(group, content, stopAt);
        LayerFilter.ApplyAll(group.Filters, content);
        PixelOps.BlendLayer(destination, content, 0, 0, group.Opacity, group.BlendMode);
        return reached || ReferenceEquals(group, stopAt);
    }

    private static void DrawLayer(Layer layer, PixelBuffer destination)
    {
        if (!LayerFilter.AnyActive(layer.Filters))
        {
            PixelOps.BlendLayer(destination, layer.Pixels, layer.OffsetX, layer.OffsetY,
                layer.Opacity, layer.BlendMode, layer.Mask);
            return;
        }

        using var filtered = layer.Pixels.Clone();
        LayerFilter.ApplyAll(layer.Filters, filtered);
        PixelOps.BlendLayer(destination, filtered, layer.OffsetX, layer.OffsetY,
            layer.Opacity, layer.BlendMode, layer.Mask);
    }

    /// <summary>
    /// A group without filters, at full opacity in Normal mode and holding only Normal content
    /// looks the same drawn straight onto what is below ("over" is associative): no extra buffer.
    /// </summary>
    private static bool CanDrawInPlace(LayerGroup group) =>
        !LayerFilter.AnyActive(group.Filters) &&
        group.Opacity >= 1f &&
        group.BlendMode == BlendMode.Normal &&
        group.SelfAndDescendants().All(n => n.BlendMode == BlendMode.Normal);
}
