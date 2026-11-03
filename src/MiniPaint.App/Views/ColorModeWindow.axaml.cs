using Avalonia.Controls;
using Avalonia.Interactivity;
using MiniPaint.App.ViewModels;
using MiniPaint.Core.ColorMode;

namespace MiniPaint.App.Views;

public partial class ColorModeWindow : Window
{
    public ColorModeWindow() : this(new ColorModeViewModel(new DocumentColorSettings()))
    {
    }

    public ColorModeWindow(ColorModeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
