using System.Windows;
using CoDTweaker.Services;
using Spock.AppKit;

namespace CoDTweaker;

public partial class App : Application
{
    /// <summary>Set by "--players &lt;folder&gt;" on the command line: use this folder instead of the saved one.</summary>
    public static string? PlayersFolderOverride { get; private set; }

    /// <summary>"--tray": start hidden in the tray (for Start with Windows).</summary>
    public static bool StartInTray { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CrashGuard.Install(this, "CoD Tweaker", AppPaths.Logs);

        int p = Array.IndexOf(e.Args, "--players");
        if (p >= 0 && p + 1 < e.Args.Length) PlayersFolderOverride = e.Args[p + 1];

        // "CoDTweaker.exe --apply preset.json": no window, just do it and report through the console or a log file.
        if (CliApply.Wanted(e.Args))
        {
            int code;
            using (var log = CliLog.Open(AppPaths.Logs, "apply"))
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

        // Other tools (CoD Dedupe's "after an update" routine) find this exe through the saved path.
        var prefs = AppSettings.Load();
        if (prefs.LastExePath != Shell.ExePath && Shell.ExePath.Length > 0) { prefs.LastExePath = Shell.ExePath; prefs.Save(); }

        Theme.Apply(Theme.FromArgsOrSetting(e.Args, prefs.Theme));

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
                Shell.SaveScreenshot(window, target);
                window.ExitApp();
            };
        }
    }
}
