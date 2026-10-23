using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoDTweaker.Services;

/// <summary>What the app knows about a setting beyond the game's own one-line description.</summary>
public sealed class SettingNote
{
    public string Note { get; set; } = "";
    /// <summary>True when the in-game menu has no control for this setting: only the file can change it.</summary>
    public bool Hidden { get; set; }
    public string? Suggested { get; set; }
}

/// <summary>
/// Notes keyed by setting name, from the built-in notes.json plus an optional notes.json in the app's
/// data folder (entries there win, so notes can be added or corrected without a new build).
/// </summary>
public static class Notes
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    static Dictionary<string, SettingNote>? _all;

    public static IReadOnlyDictionary<string, SettingNote> All => _all ??= LoadAll();

    public static SettingNote? For(string name) => All.TryGetValue(name, out var n) ? n : null;

    /// <summary>Tests swap the whole table.</summary>
    public static void Use(Dictionary<string, SettingNote>? table) => _all = table;

    static Dictionary<string, SettingNote> LoadAll()
    {
        var all = new Dictionary<string, SettingNote>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("CoDTweaker.Resources.notes.json");
            if (s != null) Merge(all, JsonSerializer.Deserialize<Dictionary<string, SettingNote>>(s, Json));
        }
        catch { /* the built-in notes are a convenience */ }
        try
        {
            if (File.Exists(AppPaths.NotesOverride))
                Merge(all, JsonSerializer.Deserialize<Dictionary<string, SettingNote>>(File.ReadAllText(AppPaths.NotesOverride), Json));
        }
        catch { /* a broken override file is ignored, the built-in notes still show */ }
        return all;
    }

    static void Merge(Dictionary<string, SettingNote> into, Dictionary<string, SettingNote>? from)
    {
        if (from == null) return;
        foreach (var (k, v) in from) if (v != null) into[k] = v;
    }
}
