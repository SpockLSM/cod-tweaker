using System.Diagnostics;

namespace CoDTweaker.Services;

public static class GameProcess
{
    public static bool IsRunning()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("cod")) { p.Dispose(); return true; }
        }
        catch { }
        return false;
    }
}
