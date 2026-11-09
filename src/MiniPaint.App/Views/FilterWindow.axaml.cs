using Avalonia.Controls;
using Avalonia.Interactivity;
using MiniPaint.App.ViewModels;

namespace MiniPaint.App.Views;

/// <summary>Settings of one filter, previewed live on the canvas; OK keeps them, Cancel drops them.</summary>
public partial class FilterWindow : Window
{
    public FilterWindow() : this(new FilterDialogViewModel(new Core.Filters.BrightnessContrastFilter(0, 0), "Layer"))
    {
    }

    public FilterWindow(FilterDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private FilterDialogViewModel Vm => (FilterDialogViewModel)DataContext!;

    private void OnResetClick(object? sender, RoutedEventArgs e) => Vm.Reset();
    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
