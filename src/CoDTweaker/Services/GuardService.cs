using System.IO;
using CoDTweaker.Models;

namespace CoDTweaker.Services;

public sealed record GuardResult(string File, int Restored, List<string> Missing, string? Backup, string? Error);

/// <summary>
/// Puts pinned values back into a settings file after the game rewrote it. Only the pinned settings
/// change; whatever else the game wrote is kept. Backs up first, and leaves the read-only flag as asked.
/// </summary>
public static class GuardService
{
    public static GuardResult Reapply(string file, IReadOnlyList<PinnedValue> pins, bool? lockAfter = null)
    {
        try
        {
            var doc = SettingsDocument.Load(file);
            var changes = new Dictionary<Setting, string>();
            var missing = new List<string>();
            foreach (var pin in pins)
            {
                var s = doc.Match(pin.Name, pin.Variant);
                if (s == null) { missing.Add(pin.Name); continue; }
                string value = s.Normalize(pin.Value) ?? pin.Value;
                if (s.OriginalValue != value) changes[s] = value;
            }

            bool wasReadOnly = (File.GetAttributes(file) & FileAttributes.ReadOnly) != 0;
            bool lockIt = lockAfter ?? wasReadOnly;
            string? backup = null;
            if (changes.Count > 0)
            {
                backup = BackupService.Create(file);
                if (wasReadOnly) File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
                File.WriteAllBytes(file, doc.RenderBytes(changes));
            }
            SetLock(file, lockIt);
            return new GuardResult(file, changes.Count, missing, backup, null);
        }
        catch (Exception ex)
        {
            return new GuardResult(file, 0, new(), null, ex.Message);
        }
    }

    public static void SetLock(string file, bool locked)
    {
        var attrs = File.GetAttributes(file);
        var wanted = locked ? attrs | FileAttributes.ReadOnly : attrs & ~FileAttributes.ReadOnly;
        if (wanted != attrs) File.SetAttributes(file, wanted);
    }

    public static bool IsLocked(string file)
    {
        try { return File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReadOnly) != 0; }
        catch { return false; }
    }
}
