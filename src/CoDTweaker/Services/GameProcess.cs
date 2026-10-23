using System.Diagnostics;

namespace CoDTweaker.Services;

public static class GameProcess
{
    /// <summary>Tests plug in a fake answer here.</summary>
    public static Func<bool>? Override { get; set; }

    public static bool IsRunning()
    {
        if (Override != null) return Override();
        try
        {
            foreach (var p in Process.GetProcessesByName("cod")) { p.Dispose(); return true; }
        }
        catch { }
        return false;
    }
}
