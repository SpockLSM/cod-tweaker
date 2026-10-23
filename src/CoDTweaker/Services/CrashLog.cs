using System.IO;
using System.Reflection;

namespace CoDTweaker.Services;

/// <summary>Writes what went wrong to a file the user can send along, instead of a bare Windows error box.</summary>
public static class CrashLog
{
    public static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";

    public static string? Write(Exception ex, string where)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Logs);
            string path = Path.Combine(AppPaths.Logs, $"crash-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
            File.WriteAllText(path,
                $"CoD Tweaker {Version}\r\n{DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\nWindows {Environment.OSVersion.VersionString}, .NET {Environment.Version}\r\n" +
                $"Where: {where}\r\n\r\n{ex}\r\n");
            Prune();
            return path;
        }
        catch { return null; }
    }

    static void Prune()
    {
        foreach (var f in new DirectoryInfo(AppPaths.Logs).GetFiles("crash-*.txt").OrderByDescending(f => f.LastWriteTime).Skip(20))
            try { f.Delete(); } catch { }
    }
}
