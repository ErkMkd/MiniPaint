using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MiniPaint.App.Native;

/// <summary>
/// Windows shell helpers so taskbar pinning / grouping use a stable AppUserModelID.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsShell
{
    /// <summary>Stable ID for taskbar grouping and pinning (must match the .lnk PropertyStore when set).</summary>
    public const string AppUserModelId = "ErkMkd.MiniPaint";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

    public static void SetAppUserModelId()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            var hr = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
            if (hr != 0)
                Logging.AppLog.Warn($"SetCurrentProcessExplicitAppUserModelID failed HRESULT=0x{hr:X8}");
            else
                Logging.AppLog.Info($"AppUserModelID={AppUserModelId}");
        }
        catch (Exception ex)
        {
            Logging.AppLog.Warn($"Could not set AppUserModelID: {ex.Message}");
        }
    }
}
