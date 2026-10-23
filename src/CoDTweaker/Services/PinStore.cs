using System.IO;
using System.Text.Json;

namespace CoDTweaker.Services;

public sealed class PinnedValue
{
    public string Name { get; set; } = "";
    public int Variant { get; set; }
    public string Value { get; set; } = "";
}

/// <summary>
/// The settings the user pinned, per file: the values the Guard writes back after the game exits.
/// Saved as pins.json next to the app's other preferences.
/// </summary>
public sealed class PinStore
{
    /// <summary>Lower-cased full path of the settings file -> its pinned values.</summary>
    public Dictionary<string, List<PinnedValue>> Files { get; set; } = new();

    static string KeyFor(string path) => Path.GetFullPath(path).ToLowerInvariant();

    public static PinStore Load()
    {
        try
        {
            if (File.Exists(AppPaths.Pins))
                return JsonSerializer.Deserialize<PinStore>(File.ReadAllText(AppPaths.Pins)) ?? new PinStore();
        }
        catch { /* unreadable pins just means none */ }
        return new PinStore();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.Pins)!);
            File.WriteAllText(AppPaths.Pins, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* pins are re-created by the user if this ever fails */ }
    }

    public IReadOnlyList<PinnedValue> For(string path) =>
        Files.TryGetValue(KeyFor(path), out var list) ? list : Array.Empty<PinnedValue>();

    public bool IsPinned(string path, string name, int variant) =>
        For(path).Any(p => p.Variant == variant && p.Name == name);

    /// <summary>Pins a setting, or updates the value it is pinned at.</summary>
    public void Set(string path, string name, int variant, string value)
    {
        string k = KeyFor(path);
        if (!Files.TryGetValue(k, out var list)) Files[k] = list = new();
        var existing = list.FirstOrDefault(p => p.Variant == variant && p.Name == name);
        if (existing == null) list.Add(new PinnedValue { Name = name, Variant = variant, Value = value });
        else existing.Value = value;
    }

    public void Remove(string path, string name, int variant)
    {
        string k = KeyFor(path);
        if (!Files.TryGetValue(k, out var list)) return;
        list.RemoveAll(p => p.Variant == variant && p.Name == name);
        if (list.Count == 0) Files.Remove(k);
    }

    public int Count(string path) => For(path).Count;

    /// <summary>Every file that has at least one pin and still exists.</summary>
    public IEnumerable<string> PinnedFiles => Files.Where(kv => kv.Value.Count > 0 && File.Exists(kv.Key)).Select(kv => kv.Key);
}
