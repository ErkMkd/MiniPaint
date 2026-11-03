using Avalonia.Controls;
using Avalonia.Input;

namespace MiniPaint.App.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = "Version " + AppInfo.DisplayVersion;
        Opened += (_, _) => Activate();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        Close();
    }
}
