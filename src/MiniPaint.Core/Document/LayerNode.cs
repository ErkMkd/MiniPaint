using MiniPaint.Core.Composition;
using MiniPaint.Core.Filters;

namespace MiniPaint.Core.Document;

/// <summary>
/// An entry of the layer stack: a pixel <see cref="Layer"/> or a <see cref="LayerGroup"/> of
/// entries. Visibility, opacity, blend mode and filters apply to the entry as a whole.
/// </summary>
public abstract class LayerNode : IDisposable
{
    protected LayerNode(string name)
    {
        Name = name;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }
    public bool IsVisible { get; set; } = true;
    public float Opacity { get; set; } = 1f;
    public BlendMode BlendMode { get; set; } = BlendMode.Normal;

    /// <summary>
    /// Non-destructive filters, applied in order when the image is composed. The list is never
    /// modified in place: changes assign a new one (so undo steps can keep the old one).
    /// </summary>
    public IReadOnlyList<LayerFilter> Filters { get; set; } = [];

    /// <summary>The group holding this entry (the document's root group at the top level).</summary>
    public LayerGroup? Parent { get; internal set; }

    /// <summary>A copy with its own pixels / children (and a new id).</summary>
    public abstract LayerNode CloneNode(string? newName = null);

    /// <summary>This entry, then everything inside it, depth-first, bottom to top.</summary>
    public IEnumerable<LayerNode> SelfAndDescendants()
    {
        yield return this;
        if (this is LayerGroup group)
        {
            foreach (var child in group.Children)
            {
                foreach (var node in child.SelfAndDescendants())
                    yield return node;
            }
        }
    }

    /// <summary>True when <paramref name="node"/> is this entry or inside it.</summary>
    public bool Contains(LayerNode node)
    {
        for (var n = node; n is not null; n = n.Parent)
        {
            if (ReferenceEquals(n, this))
                return true;
        }
        return false;
    }

    protected void CopyPropertiesTo(LayerNode copy)
    {
        copy.IsVisible = IsVisible;
        copy.Opacity = Opacity;
        copy.BlendMode = BlendMode;
        copy.Filters = Filters;
    }

    public abstract void Dispose();
}

/// <summary>
/// A folder of layers. Its content is composed on its own (isolated), then its filters, opacity
/// and blend mode apply to that result.
/// </summary>
public sealed class LayerGroup : LayerNode
{
    private readonly List<LayerNode> _children = new();
    private bool _disposed;

    public LayerGroup(string name) : base(name)
    {
    }

    /// <summary>Bottom to top.</summary>
    public IReadOnlyList<LayerNode> Children => _children;

    /// <summary>Open in the layers panel (its content listed under it).</summary>
    public bool IsExpanded { get; set; } = true;

    public override LayerNode CloneNode(string? newName = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var copy = new LayerGroup(newName ?? Name + " copy") { IsExpanded = IsExpanded };
        CopyPropertiesTo(copy);
        foreach (var child in _children)
            copy.Insert(copy._children.Count, child.CloneNode(child.Name));
        return copy;
    }

    internal void Insert(int index, LayerNode node)
    {
        if (node.Parent is not null)
            throw new InvalidOperationException("The entry is already in a group.");
        if (node is LayerGroup group && group.Contains(this))
            throw new InvalidOperationException("A group cannot go inside itself.");
        _children.Insert(Math.Clamp(index, 0, _children.Count), node);
        node.Parent = this;
    }

    internal int Remove(LayerNode node)
    {
        int index = _children.IndexOf(node);
        if (index >= 0)
        {
            _children.RemoveAt(index);
            node.Parent = null;
        }
        return index;
    }

    public override void Dispose()
    {
        if (_disposed) return;
        foreach (var child in _children)
            child.Dispose();
        _children.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
