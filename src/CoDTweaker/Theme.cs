using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace CoDTweaker;

/// <summary>Light or dark: swaps the brush dictionary every control reads through DynamicResource.</summary>
public static class Theme
{
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

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml") };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = dict; else merged.Add(dict);
        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
    }

    /// <summary>Windows 10 20H1 and later can draw the title bar dark to match.</summary>
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

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
