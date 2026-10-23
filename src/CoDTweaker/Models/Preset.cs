using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoDTweaker.Models;

/// <summary>One value in a preset. The key is kept for an exact match; name and variant find the setting when a new build renamed the key.</summary>
public sealed class PresetValue
{
    public string Name { get; set; } = "";
    public int Variant { get; set; }
    public string Value { get; set; } = "";
    public string? Key { get; set; }
}

/// <summary>
/// A set of values exported from one settings file, to apply again later: after a season renamed the file,
/// on another PC, or shared with someone. Values are matched by name and variant, so the hashes in the
/// keys may change without losing anything.
/// </summary>
public sealed class Preset
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string App { get; set; } = "CoD Tweaker";
    public int Format { get; set; } = 1;
    /// <summary>"cod26" and the like: the game the file belonged to.</summary>
    public string Game { get; set; } = "";
    /// <summary>"system", "profile", "options" or "other": which of the game's files this came from.</summary>
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
    public List<PresetValue> Values { get; set; } = new();

    public static Preset From(string game, string kind, string title, IEnumerable<(Setting Setting, string Value)> values) => new()
    {
        Game = game, Kind = kind, Title = title,
        Values = values.Select(v => new PresetValue { Name = v.Setting.Name, Variant = v.Setting.Variant, Value = v.Value, Key = v.Setting.Key }).ToList(),
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static Preset FromJson(string json)
    {
        var p = JsonSerializer.Deserialize<Preset>(json, Json) ?? throw new InvalidDataException("Not a preset file.");
        if (p.Values == null || p.Values.Count == 0) throw new InvalidDataException("The preset holds no values.");
        if (p.Values.Any(v => string.IsNullOrWhiteSpace(v.Name))) throw new InvalidDataException("A value in the preset has no setting name.");
        return p;
    }

    public void Save(string path) => File.WriteAllText(path, ToJson());
    public static Preset Load(string path) => FromJson(File.ReadAllText(path));

    /// <summary>Pairs each preset value with the setting it belongs to in the document; values with no home are listed apart.</summary>
    public (List<(Setting Setting, string Value)> Matched, List<PresetValue> Unmatched) MatchAgainst(SettingsDocument doc)
    {
        var matched = new List<(Setting, string)>();
        var unmatched = new List<PresetValue>();
        var taken = new HashSet<Setting>();
        foreach (var v in Values)
        {
            var s = doc.Match(v.Name, v.Variant, v.Key);
            if (s == null || s.IsProtected || !taken.Add(s)) { unmatched.Add(v); continue; }
            matched.Add((s, s.Normalize(v.Value) ?? v.Value));
        }
        return (matched, unmatched);
    }
}
