using System.IO;

namespace CoDTweaker.Services;

public sealed record BackupInfo(string Path, DateTime Time, long Size);

/// <summary>Keeps a copy of every settings file before it is changed. The newest 30 per file are kept.</summary>
public static class BackupService
{
    public const int Keep = 30;

    /// <summary>Tests point this somewhere disposable.</summary>
    public static string? RootOverride { get; set; }

    public static string Root => RootOverride ?? Path.Combine(AppPaths.Root, "backups");

    static string FolderFor(string file)
    {
        // players\<id>\g.p.cod26.1.0.l.txt -> <id>_g.p.cod26.1.0.l.txt ; players\s.1.0.cod26.txt -> s.1.0.cod26.txt
        string name = Path.GetFileName(file);
        string parent = Path.GetFileName(Path.GetDirectoryName(file) ?? "");
        if (parent.Length > 0 && parent.All(char.IsDigit)) name = parent + "_" + name;
        return Path.Combine(Root, name);
    }

    public static string Create(string file)
    {
        string dir = FolderFor(file);
        Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt");
        int n = 1;
        while (File.Exists(target)) target = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + $"_{n++}.txt");
        File.Copy(file, target);
        File.SetAttributes(target, FileAttributes.Normal);
        Prune(file);
        return target;
    }

    /// <summary>Deletes the oldest backups of a file beyond the newest <see cref="Keep"/>.</summary>
    public static int Prune(string file, int keep = Keep)
    {
        var old = List(file).Skip(keep).ToList();
        foreach (var b in old) { try { File.Delete(b.Path); } catch { /* a backup that won't delete is just kept */ } }
        return old.Count;
    }

    public static List<BackupInfo> List(string file)
    {
        string dir = FolderFor(file);
        if (!Directory.Exists(dir)) return new();
        return Directory.EnumerateFiles(dir, "*.txt")
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => new BackupInfo(f.FullName, f.LastWriteTime, f.Length))
            .ToList();
    }
}
