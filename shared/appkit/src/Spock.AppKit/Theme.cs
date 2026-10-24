using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Spock.AppKit;

/// <summary>
/// Light or dark. The brushes live in Themes/Light.xaml and Themes/Dark.xaml, the control styles in
/// Themes/Controls.xaml; an app merges Light.xaml and Controls.xaml in its App.xaml and calls
/// <see cref="Apply"/> at startup, which swaps the first merged dictionary for the chosen brushes.
/// Every control reads its brushes through DynamicResource, so the swap repaints everything.
/// </summary>
public static class Theme
{
    public const string BrushesUri = "pack://application:,,,/Spock.AppKit;component/Themes/{0}.xaml";
    public const string ControlsUri = "pack://application:,,,/Spock.AppKit;component/Themes/Controls.xaml";

    public static bool IsDark { get; private set; }

    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    /// <summary>"dark", "light", or anything else for "follow Windows".</summary>
    public static bool FromSetting(string? setting) => setting switch { "dark" => true, "light" => false, _ => SystemPrefersDark() };

    /// <summary>Reads "--theme dark|light" from the command line, else the saved setting, else Windows.</summary>
    public static bool FromArgsOrSetting(string[] args, string? setting)
    {
        int t = Array.IndexOf(args, "--theme");
        return FromSetting(t >= 0 && t + 1 < args.Length ? args[t + 1] : setting);
    }

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var brushes = new ResourceDictionary { Source = new Uri(string.Format(BrushesUri, dark ? "Dark" : "Light")) };
        var merged = Application.Current.Resources.MergedDictionaries;
        int at = IndexOfBrushes(merged);
        if (at >= 0) merged[at] = brushes; else merged.Insert(0, brushes);
        if (!merged.Any(d => d.Source != null && d.Source.OriginalString.EndsWith("Controls.xaml", StringComparison.OrdinalIgnoreCase)))
            merged.Add(new ResourceDictionary { Source = new Uri(ControlsUri) });
        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
    }

    static int IndexOfBrushes(IList<ResourceDictionary> merged)
    {
        for (int i = 0; i < merged.Count; i++)
        {
            string s = merged[i].Source?.OriginalString ?? "";
            if (s.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) || s.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    /// <summary>Windows 10 20H1 and later can draw the title bar dark to match. Call from SourceInitialized.</summary>
    public static void ApplyTitleBar(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int value = IsDark ? 1 : 0;
            DwmSetWindowAttribute(handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref value, sizeof(int));
        }
        catch { }
    }

    /// <summary>The label for a theme toggle button.</summary>
    public static string ToggleLabel => IsDark ? "☀ Light mode" : "\U0001F319 Dark mode";

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
