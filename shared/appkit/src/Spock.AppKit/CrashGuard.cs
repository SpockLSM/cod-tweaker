using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Spock.AppKit;

/// <summary>
/// Turns an unhandled exception into a log file the user can send along and a plain message, instead of
/// Windows' bare error box. Call <see cref="Install"/> first thing in OnStartup.
/// </summary>
public static class CrashGuard
{
    static bool _reporting;

    /// <param name="logsFolder">Where crash-*.txt files go; created on demand. The newest 20 are kept.</param>
    public static void Install(Application app, string appName, string logsFolder)
    {
        app.DispatcherUnhandledException += (_, e) => { e.Handled = true; Report(appName, logsFolder, e.Exception, "window"); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(appName, logsFolder, e.ExceptionObject as Exception ?? new Exception("Unknown error"), "background");
        TaskScheduler.UnobservedTaskException += (_, e) => { Write(logsFolder, appName, e.Exception, "task"); e.SetObserved(); };
    }

    static void Report(string appName, string logsFolder, Exception ex, string where)
    {
        if (_reporting) return;
        _reporting = true;
        string? log = Write(logsFolder, appName, ex, where);
        try
        {
            MessageBox.Show(
                $"{appName} hit a problem and has to close. Your files were not touched by this error.\n\n" +
                (log != null ? $"Details were saved to:\n{log}\n\n" : "") +
                $"{ex.GetType().Name}: {ex.Message}",
                appName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
        Environment.Exit(1);
    }

    /// <summary>Writes the crash file and returns its path, or null when even that failed.</summary>
    public static string? Write(string logsFolder, string appName, Exception ex, string where)
    {
        try
        {
            Directory.CreateDirectory(logsFolder);
            string path = Path.Combine(logsFolder, $"crash-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
            File.WriteAllText(path,
                $"{appName} {AppVersion.Informational}\r\n{DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n" +
                $"Windows {Environment.OSVersion.VersionString}, .NET {Environment.Version}\r\nWhere: {where}\r\n\r\n{ex}\r\n");
            Prune(logsFolder);
            return path;
        }
        catch { return null; }
    }

    static void Prune(string logsFolder)
    {
        try
        {
            foreach (var f in new DirectoryInfo(logsFolder).GetFiles("crash-*.txt").OrderByDescending(f => f.LastWriteTime).Skip(20))
                try { f.Delete(); } catch { }
        }
        catch { }
    }
}
