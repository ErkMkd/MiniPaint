using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MiniPaint.App.Logging;
using MiniPaint.App.ViewModels;
using MiniPaint.App.Views;

namespace MiniPaint.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        AppLog.Info("App.Initialize");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppLog.InstallAvaloniaHandlers();
        AppLog.Info("App.OnFrameworkInitializationCompleted");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Startup += (_, e) => AppLog.Info($"Desktop Startup args=[{string.Join(", ", e.Args)}]");
            desktop.Exit += (_, e) => AppLog.Info($"Desktop Exit code={e.ApplicationExitCode}");

            try
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainViewModel(),
                };
                AppLog.Info("MainWindow created");
            }
            catch (Exception ex)
            {
                AppLog.Fatal("Failed to create MainWindow", ex);
                throw;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
