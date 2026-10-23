namespace CoDTweaker.Models;

/// <summary>A value from "the other side" of a comparison: another file, a backup or a preset.</summary>
public sealed record ValueRef(string? Key, string Name, int Variant, string Value);

public sealed record DiffResult(List<(Setting Mine, string Theirs)> Pairs, List<ValueRef> OnlyInOther, int OnlyInMine);

/// <summary>Lines up two sets of values setting by setting, by exact key first and by name + variant after.</summary>
public static class Diff
{
    public static IEnumerable<ValueRef> Refs(SettingsDocument doc) =>
        doc.AllSettings.Select(s => new ValueRef(s.Key, s.Name, s.Variant, s.OriginalValue));

    public static IEnumerable<ValueRef> Refs(Preset preset) =>
        preset.Values.Select(v => new ValueRef(v.Key, v.Name, v.Variant, v.Value));

    public static DiffResult Pair(SettingsDocument mine, IEnumerable<ValueRef> theirs)
    {
        var pairs = new List<(Setting, string)>();
        var onlyInOther = new List<ValueRef>();
        var taken = new HashSet<Setting>();
        foreach (var t in theirs)
        {
            var s = mine.Match(t.Name, t.Variant, t.Key);
            if (s == null || !taken.Add(s)) { onlyInOther.Add(t); continue; }
            pairs.Add((s, t.Value));
        }
        int onlyInMine = mine.AllSettings.Count() - taken.Count;
        return new DiffResult(pairs, onlyInOther, onlyInMine);
    }
}
