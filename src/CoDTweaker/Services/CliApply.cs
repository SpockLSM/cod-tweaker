using System.IO;
using System.Runtime.InteropServices;
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

    /// <summary>A window app has no console; borrow the parent's when started from one, and keep a log file either way.</summary>
    public static TextWriter OpenLog()
    {
        var writers = new List<TextWriter>();
        try
        {
            if (AttachConsole(-1))
            {
                var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                stdout.WriteLine();
                writers.Add(stdout);
            }
        }
        catch { }
        try
        {
            Directory.CreateDirectory(AppPaths.Logs);
            writers.Add(new StreamWriter(Path.Combine(AppPaths.Logs, $"apply-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt")) { AutoFlush = true });
        }
        catch { }
        return new TeeWriter(writers);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AttachConsole(int processId);

    sealed class TeeWriter : TextWriter
    {
        readonly List<TextWriter> _w;
        public TeeWriter(List<TextWriter> w) => _w = w;
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Write(char value) { foreach (var w in _w) w.Write(value); }
        public override void Write(string? value) { foreach (var w in _w) w.Write(value); }
        public override void WriteLine(string? value) { foreach (var w in _w) w.WriteLine(value); }
        protected override void Dispose(bool disposing) { if (disposing) foreach (var w in _w) w.Dispose(); base.Dispose(disposing); }
    }
}
