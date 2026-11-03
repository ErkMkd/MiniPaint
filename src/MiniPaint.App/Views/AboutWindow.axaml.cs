using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MiniPaint.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = "Version " + GetDisplayVersion();
    }

    private static string GetDisplayVersion()
    {
        var asm = typeof(AboutWindow).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // Strip optional "+git..." suffix from some SDK formats
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        var ver = asm.GetName().Version;
        return ver is null ? "unknown" : $"{ver.Major}.{ver.Minor}.{ver.Build}";
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
