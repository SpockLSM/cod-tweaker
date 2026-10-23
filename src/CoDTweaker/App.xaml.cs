using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CoDTweaker.Services;

namespace CoDTweaker;

public partial class App : Application
{
    /// <summary>Set by "--players &lt;folder&gt;" on the command line: use this folder instead of the saved one.</summary>
    public static string? PlayersFolderOverride { get; private set; }

    /// <summary>"--tray": start hidden in the tray (for a shortcut in the Startup folder).</summary>
    public static bool StartInTray { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ReportCrash(args.ExceptionObject as Exception ?? new Exception("Unknown error"), "background", fatal: true);
        TaskScheduler.UnobservedTaskException += (_, args) => { CrashLog.Write(args.Exception, "task"); args.SetObserved(); };

        int p = Array.IndexOf(e.Args, "--players");
        if (p >= 0 && p + 1 < e.Args.Length) PlayersFolderOverride = e.Args[p + 1];

        // "CoDTweaker.exe --apply preset.json": no window, just do it and report through the console or a log file.
        if (CliApply.Wanted(e.Args))
        {
            int code;
            using (var log = CliApply.OpenLog())
            {
                var args = e.Args.ToList();
                if (PlayersFolderOverride != null && !args.Contains("--players")) { args.Add("--players"); args.Add(PlayersFolderOverride); }
                code = CliApply.Run(args.ToArray(), log);
            }
            Shutdown(code);
            return;
        }

        StartInTray = Array.IndexOf(e.Args, "--tray") >= 0;

        // A documentation screenshot should show the app, not whether the game happens to be open right now.
        if (Array.IndexOf(e.Args, "--screenshot") >= 0) GameProcess.Override = () => false;

        // "--theme dark|light" for documentation; otherwise the saved choice, or whatever Windows is set to.
        int t = Array.IndexOf(e.Args, "--theme");
        Theme.Apply(Theme.FromSetting(t >= 0 && t + 1 < e.Args.Length ? e.Args[t + 1] : AppSettings.Load().Theme));

        var window = new MainWindow();
        if (!StartInTray || !window.CanHideToTray) window.Show();
        else window.HideToTray(firstTime: false);

        // Hidden helper for documentation and the CI smoke test: CoDTweaker.exe --screenshot out.png
        int i = Array.IndexOf(e.Args, "--screenshot");
        if (i >= 0 && i + 1 < e.Args.Length)
        {
            string target = e.Args[i + 1];
            window.ContentRendered += async (_, _) =>
            {
                await Task.Delay(1200);
                SaveScreenshot(window, target);
                window.ExitApp();
            };
        }
    }

    void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ReportCrash(e.Exception, "window", fatal: true);
    }

    static bool _reporting;
    void ReportCrash(Exception ex, string where, bool fatal)
    {
        if (_reporting) return;
        _reporting = true;
        string? log = CrashLog.Write(ex, where);
        try
        {
            MessageBox.Show(
                "CoD Tweaker hit a problem and has to close. Your settings files were not touched by this error.\n\n" +
                (log != null ? $"Details were saved to:\n{log}\n\n" : "") +
                $"{ex.GetType().Name}: {ex.Message}",
                "CoD Tweaker", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
        if (fatal) Environment.Exit(1);
    }

    static void SaveScreenshot(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(root);
        double w = root.ActualWidth + root.Margin.Left + root.Margin.Right;
        double h = root.ActualHeight + root.Margin.Top + root.Margin.Bottom;
        // Paint through a brush so the element's margin doesn't shift the capture.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(root.Margin.Left, root.Margin.Top, root.ActualWidth, root.ActualHeight));
        }
        var bmp = new RenderTargetBitmap((int)(w * dpi.DpiScaleX), (int)(h * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
