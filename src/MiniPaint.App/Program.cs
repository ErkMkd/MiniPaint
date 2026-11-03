using System;
using Avalonia;
using MiniPaint.App.Logging;
using MiniPaint.App.Native;

namespace MiniPaint.App;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            AppLog.Initialize();
            AppLog.InstallGlobalHandlers();
            // Before any HWND: stable taskbar pin / grouping id.
            if (OperatingSystem.IsWindows())
                WindowsShell.SetAppUserModelId();
            AppLog.Info($"Args: [{string.Join(", ", args)}]");

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);

            AppLog.Info("Main loop exited normally");
        }
        catch (Exception ex)
        {
            AppLog.Fatal("Unhandled exception in Program.Main", ex);
            throw;
        }
        finally
        {
            AppLog.Shutdown();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace()
            .AfterSetup(_ => AppLog.Info("Avalonia AfterSetup complete"));
}
