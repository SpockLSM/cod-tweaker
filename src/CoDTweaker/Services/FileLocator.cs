using System.IO;
using System.Text.RegularExpressions;
using CoDTweaker.Models;

namespace CoDTweaker.Services;

/// <param name="Kind">"system", "profile", "options" or "other": which of the game's files this is.</param>
/// <param name="Game">The game token from the name, e.g. "cod26"; empty when the name doesn't carry one.</param>
public sealed record SettingsFileInfo(string Path, string Title, string Subtitle, string Group, int Order, string Kind = "other", string Game = "")
{
    /// <summary>The game's own files, as opposed to copies the user made or opened by hand.</summary>
    public bool IsGameFile => Group != "Other copies" && Group != "Other files";
}

/// <summary>
/// Finds Call of Duty's settings files. A file counts if its contents are in the game's settings
/// format, whatever it is called, so new seasons, new games and renamed files still show up.
/// </summary>
public static class FileLocator
{
    // s.1.0.cod26.txt -> system settings (graphics, display, audio devices); the numbers change between builds
    static readonly Regex SystemRx = new(@"^s\.[\d.]+\.(cod\d+)\.txt$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // g.p.cod26.1.0.l.txt, g.p.cod26.pm.1.0.l.txt -> the player's profile (controls, FOV, audio mix)
    static readonly Regex ProfileRx = new(@"^g\.p\.(cod\d+)(\.[a-z][a-z0-9]*)?\.[\d.]+\.l\.txt$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // options.4.cod23.txt -> the 2022-2024 games
    static readonly Regex OptionsRx = new(@"^options\.[\d.]+\.(cod\d+)\.txt$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex GamerProfileRx = new(@"^gamerprofile.*\.(cod\d+)\.txt$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex GameTokenRx = new(@"\bcod\d{2}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex CopyRx = new(@"bak|backup|copy|old|orig|before|\(\d+\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Where the game keeps its files: the current location first, then the one older games used.</summary>
    public static IEnumerable<string> DefaultFolders
    {
        get
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Activision", "Call of Duty", "players");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Call of Duty", "players");
        }
    }

    public static string DefaultPlayersFolder => DefaultFolders.FirstOrDefault(Directory.Exists) ?? DefaultFolders.First();

    public static List<SettingsFileInfo> Find(string playersFolder)
    {
        var list = new List<SettingsFileInfo>();
        if (!Directory.Exists(playersFolder)) return list;
        Scan(playersFolder, "Game settings", list);
        foreach (var dir in SafeDirs(playersFolder))
            Scan(dir, "Player " + Path.GetFileName(dir), list);
        return list.OrderBy(f => f.Order).ThenBy(f => f.Group, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList(); }
        catch { return Array.Empty<string>(); }
    }

    static void Scan(string dir, string group, List<SettingsFileInfo> list)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir, "*.txt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(); }
        catch { return; }
        foreach (var f in files)
        {
            var sections = Sniff(f);
            if (sections != null) list.Add(Classify(f, group, sections));
        }
    }

    /// <summary>The file's section names if it is in the game's settings format, otherwise null.</summary>
    public static List<string>? Sniff(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 4_000_000) return null;
            var doc = SettingsDocument.Load(path);
            return doc.AllSettings.Any() ? doc.Sections.Select(s => s.Name).ToList() : null;
        }
        catch { return null; }
    }

    public static SettingsFileInfo Classify(string path, string group, List<string> sections)
    {
        string name = Path.GetFileName(path);
        // "Sections: Audio, Display, Gameplay..." cut at a section boundary, not mid-word.
        string subtitle = "";
        if (sections.Count > 0)
        {
            var shown = new List<string>();
            foreach (var s in sections)
            {
                if (shown.Count > 0 && string.Join(", ", shown).Length + s.Length > 55) break;
                shown.Add(s);
            }
            subtitle = "Sections: " + string.Join(", ", shown) + (shown.Count < sections.Count ? "..." : "");
        }

        string game = GameTokenRx.Match(name) is { Success: true } g ? g.Value.ToLowerInvariant() : "";

        // A second ".txt" in the name, or words like "bak", mean someone's own copy of a settings file.
        string stem = name[..^4];
        bool isCopy = stem.Contains(".txt", StringComparison.OrdinalIgnoreCase) || CopyRx.IsMatch(stem);
        if (isCopy) return new SettingsFileInfo(path, name, subtitle, "Other copies", 9, "other", game);

        Match m;
        if ((m = SystemRx.Match(name)).Success) return new(path, $"System settings ({m.Groups[1].Value})", subtitle, group, 1, "system", game);
        if ((m = OptionsRx.Match(name)).Success) return new(path, $"Options ({m.Groups[1].Value})", subtitle, group, 1, "options", game);
        if ((m = ProfileRx.Match(name)).Success)
        {
            string tag = m.Groups[2].Success ? ", " + m.Groups[2].Value.TrimStart('.') : "";
            return new(path, $"Player profile ({m.Groups[1].Value}{tag})", subtitle, group, 2, "profile", game);
        }
        if ((m = GamerProfileRx.Match(name)).Success) return new(path, $"Player profile ({m.Groups[1].Value})", subtitle, group, 2, "profile", game);

        // Unknown name but the right format: show it under its own name.
        return new(path, name, subtitle, group, 3, "other", game);
    }

    public static SettingsFileInfo Describe(string path) =>
        Classify(path, "Other files", Sniff(path) ?? new List<string>());

    /// <summary>A folder path with the user's profile folders shortened, for display.</summary>
    public static string DisplayPath(string path)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (local.Length > 0 && path.StartsWith(local, StringComparison.OrdinalIgnoreCase)) return "%LOCALAPPDATA%" + path[local.Length..];
        if (profile.Length > 0 && path.StartsWith(profile, StringComparison.OrdinalIgnoreCase)) return "%USERPROFILE%" + path[profile.Length..];
        return path;
    }
}
