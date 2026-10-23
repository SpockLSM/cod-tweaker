using System.IO;
using System.Text.RegularExpressions;

namespace CoDTweaker.Services;

public sealed record SettingsFileInfo(string Path, string Title, string Subtitle, string Group);

/// <summary>Finds Call of Duty's settings files.</summary>
public static class FileLocator
{
    // s.1.0.cod26.txt  -> system settings (graphics, display, audio devices) for that game build
    static readonly Regex SystemRx = new(@"^s\.\d+\.\d+\.(cod\d+)\.txt$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // g.p.cod26.1.0.l.txt / g.p.cod26.pm.1.0.l.txt -> the player's profile (controls, FOV, audio mix)
    static readonly Regex ProfileRx = new(@"^g\.p\.(cod\d+)(\.pm)?\.\d+\.\d+\.l\.txt$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string DefaultPlayersFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Activision", "Call of Duty", "players");

    public static List<SettingsFileInfo> Find(string playersFolder)
    {
        var list = new List<SettingsFileInfo>();
        if (!Directory.Exists(playersFolder)) return list;

        foreach (var f in Directory.EnumerateFiles(playersFolder, "*.txt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var m = SystemRx.Match(Path.GetFileName(f));
            if (m.Success)
                list.Add(new SettingsFileInfo(f, $"System settings ({m.Groups[1].Value})", "Graphics, display, audio devices, FPS cap", "Shared"));
        }
        foreach (var dir in Directory.EnumerateDirectories(playersFolder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            string id = Path.GetFileName(dir);
            if (!id.All(char.IsDigit)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.txt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var m = ProfileRx.Match(Path.GetFileName(f));
                if (!m.Success) continue;
                bool pm = m.Groups[2].Success;
                list.Add(new SettingsFileInfo(f,
                    pm ? $"Field of view ({m.Groups[1].Value})" : $"Player profile ({m.Groups[1].Value})",
                    pm ? "FOV values the game keeps separately" : "Controls, sensitivity, audio mix, HDR, FOV",
                    $"Player {id}"));
            }
        }
        return list;
    }

    public static SettingsFileInfo Describe(string path) =>
        new(path, Path.GetFileName(path), Path.GetDirectoryName(path) ?? "", "Other files");
}
