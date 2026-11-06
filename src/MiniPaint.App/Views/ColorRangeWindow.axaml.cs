using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MiniPaint.App.ViewModels;

namespace MiniPaint.App.Views;

public partial class ColorRangeWindow : Window
{
    public ColorRangeWindow() : this(new ColorRangeViewModel())
    {
    }

    public ColorRangeWindow(ColorRangeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private ColorRangeViewModel? Vm => DataContext as ColorRangeViewModel;

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is null || sender is not Control preview || preview.Bounds.Width <= 0 || preview.Bounds.Height <= 0)
            return;

        var p = e.GetPosition(preview);
        Vm.PickAt(
            p.X / preview.Bounds.Width,
            p.Y / preview.Bounds.Height,
            add: e.KeyModifiers.HasFlag(KeyModifiers.Shift),
            subtract: e.KeyModifiers.HasFlag(KeyModifiers.Alt));
        e.Handled = true;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (Vm?.HasColors == true)
            Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
