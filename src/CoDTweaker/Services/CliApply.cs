using System.IO;
using CoDTweaker.Models;

namespace CoDTweaker.Services;

/// <summary>
/// CoDTweaker.exe --apply preset.json [--file settings.txt] [--players folder] [--lock | --unlock] [--dry-run]
/// Applies a preset without opening a window, for scripts that run after a game update.
/// Exit codes: 0 applied (or nothing to change), 1 error, 2 no matching settings file.
/// </summary>
public static class CliApply
{
    public static bool Wanted(string[] args) => Array.IndexOf(args, "--apply") >= 0;

    static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public static int Run(string[] args, TextWriter log)
    {
        string? presetPath = Arg(args, "--apply");
        if (presetPath == null) { log.WriteLine("Usage: CoDTweaker.exe --apply preset.json [--file settings.txt] [--players folder] [--lock | --unlock] [--dry-run]"); return 1; }

        Preset preset;
        try { preset = Preset.Load(presetPath); }
        catch (Exception ex) { log.WriteLine($"Couldn't read the preset: {ex.Message}"); return 1; }

        bool dryRun = Array.IndexOf(args, "--dry-run") >= 0;
        bool? lockAfter = Array.IndexOf(args, "--lock") >= 0 ? true : Array.IndexOf(args, "--unlock") >= 0 ? false : null;

        List<string> targets;
        string? file = Arg(args, "--file");
        if (file != null)
        {
            if (!File.Exists(file)) { log.WriteLine($"No such file: {file}"); return 2; }
            targets = new() { file };
        }
        else
        {
            string folder = Arg(args, "--players") ?? (AppSettings.Load().PlayersFolder is { Length: > 0 } p && Directory.Exists(p) ? p : FileLocator.DefaultPlayersFolder);
            targets = FileLocator.Find(folder)
                .Where(f => f.IsGameFile && (preset.Kind.Length == 0 || f.Kind == preset.Kind) && (preset.Game.Length == 0 || f.Game == preset.Game))
                .Select(f => f.Path).ToList();
            if (targets.Count == 0) { log.WriteLine($"No {preset.Kind} settings file for {preset.Game} found in {folder}."); return 2; }
        }

        log.WriteLine($"Preset: {preset.Title} ({preset.Values.Count} values, made {preset.Created:yyyy-MM-dd HH:mm})");
        int code = 0;
        foreach (var target in targets)
        {
            try
            {
                var doc = SettingsDocument.Load(target);
                var (matched, unmatched) = preset.MatchAgainst(doc);
                var changes = matched.Where(m => m.Setting.OriginalValue != m.Value).ToDictionary(m => m.Setting, m => m.Value);
                log.WriteLine($"{target}: {changes.Count} value(s) to change, {matched.Count - changes.Count} already in place, {unmatched.Count} not in this file.");
                foreach (var (s, v) in changes) log.WriteLine($"  {s.Name}{(s.Variant == 0 ? "" : $" [variant {s.Variant}]")}: {s.OriginalValue} -> {v}");
                if (dryRun) continue;

                bool wasLocked = GuardService.IsLocked(target);
                if (changes.Count > 0)
                {
                    string backup = BackupService.Create(target);
                    GuardService.SetLock(target, false);
                    File.WriteAllBytes(target, doc.RenderBytes(changes));
                    log.WriteLine($"  Saved. Backup: {backup}");
                }
                GuardService.SetLock(target, lockAfter ?? wasLocked);
                log.WriteLine("  File is " + (GuardService.IsLocked(target) ? "locked (read-only)." : "unlocked."));
            }
            catch (Exception ex) { log.WriteLine($"{target}: failed: {ex.Message}"); code = 1; }
        }
        return code;
    }

}
