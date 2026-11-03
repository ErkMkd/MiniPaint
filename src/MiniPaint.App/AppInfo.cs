using System.Reflection;

namespace MiniPaint.App;

/// <summary>Shared app identity / version strings for About + splash.</summary>
public static class AppInfo
{
    public static string DisplayVersion
    {
        get
        {
            var asm = typeof(AppInfo).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                var plus = info.IndexOf('+');
                return plus >= 0 ? info[..plus] : info;
            }

            var ver = asm.GetName().Version;
            return ver is null ? "unknown" : $"{ver.Major}.{ver.Minor}.{ver.Build}";
        }
    }
}
