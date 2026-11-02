using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MiniPaint.App.Logging;

/// <summary>
/// Simple thread-safe file logger. Logs live under %LocalAppData%\MiniPaint\logs\
/// Fatal crashes also rewrite crash-last.txt for quick inspection.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static StreamWriter? _writer;
    private static bool _installed;
    private static string? _sessionFile;

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MiniPaint", "logs");

    public static string? CurrentLogFile => _sessionFile;

    public static string CrashLastPath => Path.Combine(LogDirectory, "crash-last.txt");

    public static void Initialize()
    {
        Directory.CreateDirectory(LogDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        _sessionFile = Path.Combine(LogDirectory, $"minipaint-{stamp}.log");

        lock (Gate)
        {
            _writer = new StreamWriter(
                new FileStream(_sessionFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
                Encoding.UTF8)
            {
                AutoFlush = true
            };
        }

        Info("=== MiniPaint session start ===");
        Info($"Time (local): {DateTime.Now:O}");
        Info($"OS: {RuntimeInformation.OSDescription} | {RuntimeInformation.OSArchitecture}");
        Info($"Runtime: {RuntimeInformation.FrameworkDescription}");
        Info($"Process: {Environment.ProcessPath}");
        Info($"PID: {Environment.ProcessId}");
        Info($"CWD: {Environment.CurrentDirectory}");
        Info($"Log file: {_sessionFile}");
        Info($"Base dir: {AppContext.BaseDirectory}");

        try
        {
            var asm = typeof(AppLog).Assembly;
            var ver = asm.GetName().Version;
            Info($"Assembly: {asm.GetName().Name} {ver}");
        }
        catch (Exception ex)
        {
            Warn($"Could not read assembly version: {ex.Message}");
        }

        PruneOldLogs(keep: 20);
    }

    public static void InstallGlobalHandlers()
    {
        if (_installed) return;
        _installed = true;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Fatal("AppDomain.UnhandledException (IsTerminating=" + e.IsTerminating + ")", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Error("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Info("ProcessExit");
            Shutdown();
        };
    }

    /// <summary>Hook Avalonia UI-thread exceptions (call once UI dispatcher exists).</summary>
    public static void InstallAvaloniaHandlers()
    {
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                Fatal("Dispatcher.UIThread.UnhandledException", e.Exception);
                // Keep false so Avalonia still tears down — we already logged.
                e.Handled = false;
            };
            Info("Avalonia UIThread unhandled-exception handler installed");
        }
        catch (Exception ex)
        {
            Warn($"Failed to install Avalonia handler: {ex.Message}");
        }
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);
    public static void Fatal(string message, Exception? ex = null)
    {
        Write("FATAL", message, ex);
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"MiniPaint crash — {DateTime.Now:O}");
            sb.AppendLine(message);
            if (ex is not null)
            {
                sb.AppendLine(ex.ToString());
                if (ex.InnerException is not null)
                    sb.AppendLine("--- Inner ---");
                sb.AppendLine(ex.InnerException?.ToString() ?? "");
            }
            sb.AppendLine();
            sb.AppendLine($"Session log: {_sessionFile}");
            sb.AppendLine($"Log dir: {LogDirectory}");
            File.WriteAllText(CrashLastPath, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Last-resort: ignore secondary failures while crashing.
        }
    }

    public static void OpenLogFolder()
    {
        Directory.CreateDirectory(LogDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = LogDirectory,
            UseShellExecute = true
        });
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            try { _writer?.Flush(); _writer?.Dispose(); }
            catch { /* ignore */ }
            _writer = null;
        }
    }

    private static void Write(string level, string message, Exception? ex)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        if (ex is not null)
            line += Environment.NewLine + ex;

        lock (Gate)
        {
            try
            {
                _writer?.WriteLine(line);
            }
            catch
            {
                // Swallow I/O errors so logging never takes down the app.
            }
        }

        Debug.WriteLine(line);
    }

    private static void PruneOldLogs(int keep)
    {
        try
        {
            var files = Directory.GetFiles(LogDirectory, "minipaint-*.log")
                .OrderByDescending(f => f)
                .Skip(keep)
                .ToArray();
            foreach (var f in files)
            {
                try { File.Delete(f); } catch { /* ignore */ }
            }
        }
        catch
        {
            // ignore
        }
    }
}
