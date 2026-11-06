using Avalonia.Controls;
using Avalonia.Interactivity;
using MiniPaint.App.ViewModels;

namespace MiniPaint.App.Views;

public partial class ResizeAllWindow : Window
{
    public ResizeAllWindow() : this(new ResizeAllViewModel())
    {
    }

    public ResizeAllWindow(ResizeAllViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private ResizeAllViewModel? Vm => DataContext as ResizeAllViewModel;

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || !Vm.TryCommit())
            return;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
