using CommunityToolkit.Mvvm.ComponentModel;
using MiniPaint.Core.Document;

namespace MiniPaint.App.ViewModels;

public partial class LayerItemViewModel : ViewModelBase
{
    public Layer Layer { get; }

    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial float Opacity { get; set; }
    [ObservableProperty] public partial bool IsActive { get; set; }

    public Guid Id => Layer.Id;

    public LayerItemViewModel(Layer layer, bool isActive)
    {
        Layer = layer;
        Name = layer.Name;
        IsVisible = layer.IsVisible;
        Opacity = layer.Opacity;
        IsActive = isActive;
    }

    public void SyncFromModel()
    {
        Name = Layer.Name;
        IsVisible = Layer.IsVisible;
        Opacity = Layer.Opacity;
    }
}
