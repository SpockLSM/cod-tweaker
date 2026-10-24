using Microsoft.Win32;

namespace Spock.AppKit;

/// <summary>"Start with Windows" through the current user's Run key. No admin rights needed.</summary>
public static class StartupRun
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The command registered under this name, or null.</summary>
    public static string? Get(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(name) as string;
        }
        catch { return null; }
    }

    public static bool IsSet(string name) => Get(name) != null;

    /// <summary>Registers "exePath" plus arguments to run at logon.</summary>
    public static bool Set(string name, string exePath, string arguments = "")
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            key.SetValue(name, $"\"{exePath}\"{(arguments.Length > 0 ? " " + arguments : "")}");
            return true;
        }
        catch { return false; }
    }

    public static bool Clear(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
            return true;
        }
        catch { return false; }
    }
}
