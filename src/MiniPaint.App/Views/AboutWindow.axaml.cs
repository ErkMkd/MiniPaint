using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MiniPaint.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
