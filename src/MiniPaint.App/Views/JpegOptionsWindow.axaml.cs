using Avalonia.Controls;
using Avalonia.Interactivity;
using MiniPaint.App.ViewModels;

namespace MiniPaint.App.Views;

public partial class JpegOptionsWindow : Window
{
    public JpegOptionsWindow() : this(new JpegOptionsViewModel())
    {
    }

    public JpegOptionsWindow(JpegOptionsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
