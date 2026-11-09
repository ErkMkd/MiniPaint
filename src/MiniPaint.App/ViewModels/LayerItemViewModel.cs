using CommunityToolkit.Mvvm.ComponentModel;
using MiniPaint.Core.Composition;
using MiniPaint.Core.Document;
using MiniPaint.Core.Filters;

namespace MiniPaint.App.ViewModels;

/// <summary>One row of the layers panel: a layer or a group, indented by its nesting level.</summary>
public partial class LayerItemViewModel : ViewModelBase
{
    private const double IndentStep = 14;

    public LayerNode Node { get; }

    /// <summary>Nesting level (0 = top level).</summary>
    public int Depth { get; }

    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial float Opacity { get; set; }
    [ObservableProperty] public partial BlendMode BlendMode { get; set; }
    [ObservableProperty] public partial bool IsActive { get; set; }
    [ObservableProperty] public partial bool HasMask { get; set; }
    [ObservableProperty] public partial bool HasFilters { get; set; }
    [ObservableProperty] public partial bool IsExpanded { get; set; }

    public string BlendModeLabel => BlendModeNames.ToLabel(BlendMode);

    public Guid Id => Node.Id;

    public bool IsGroup => Node is LayerGroup;

    public double IndentWidth => Depth * IndentStep;

    public string ExpanderGlyph => IsExpanded ? "▾" : "▸";

    public LayerItemViewModel(LayerNode node, int depth, bool isActive)
    {
        Node = node;
        Depth = depth;
        Name = node.Name;
        IsActive = isActive;
        SyncFromModel();
    }

    public void SyncFromModel()
    {
        Name = Node.Name;
        IsVisible = Node.IsVisible;
        Opacity = Node.Opacity;
        BlendMode = Node.BlendMode;
        HasMask = Node is Layer { HasMask: true };
        HasFilters = Node.Filters.Count > 0;
        IsExpanded = Node is LayerGroup { IsExpanded: true };
        OnPropertyChanged(nameof(BlendModeLabel));
    }

    partial void OnBlendModeChanged(BlendMode value) => OnPropertyChanged(nameof(BlendModeLabel));
    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpanderGlyph));
}

/// <summary>One filter of the active layer or group, in the filters list.</summary>
public sealed class FilterItemViewModel(LayerFilter filter, int index)
{
    public LayerFilter Filter { get; } = filter;

    /// <summary>Position in the entry's filter list (applied in this order).</summary>
    public int Index { get; } = index;

    public string Name => Filter.Name;
    public string Summary => Filter.Summary;
    public bool IsEnabled => Filter.Enabled;
}
