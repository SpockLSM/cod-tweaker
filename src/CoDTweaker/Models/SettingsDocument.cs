using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CoDTweaker.Models;

public enum SettingKind { Bool, Number, Choice, Text }

/// <summary>One "Key = value // constraint" line of a Call of Duty settings file.</summary>
public sealed class Setting
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public required int LineIndex { get; init; }
    /// <summary>Everything before " = ", e.g. "Fov@0;24892;9411".</summary>
    public required string Key { get; init; }
    /// <summary>The readable part of the key, e.g. "Fov".</summary>
    public required string Name { get; init; }
    /// <summary>The number after '@'. The game keeps several variants of some settings.</summary>
    public required int Variant { get; init; }
    public required string Description { get; init; }
    public required string OriginalValue { get; init; }
    /// <summary>The text after " // ", e.g. "0.000000 to 1.000000" or "one of Off, On".</summary>
    public string? Constraint { get; init; }
    /// <summary>Older games wrote values in double quotes; they go back the same way.</summary>
    public bool Quoted { get; init; }
    public SettingKind Kind { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public bool IsFloat { get; init; }
    public int Decimals { get; init; }
    public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();
    /// <summary>The game marks values it detects itself with "DO NOT MODIFY".</summary>
    public bool IsProtected { get; init; }

    public bool HasSlider => Kind == SettingKind.Number && !IsProtected && Max > Min && Max - Min <= 10000;

    public string FormatNumber(double v)
    {
        v = Math.Clamp(v, Min, Max);
        return IsFloat ? v.ToString("F" + Decimals, Inv) : Math.Round(v).ToString(Inv);
    }

    /// <summary>Returns the canonical form of a user-entered value, or null if it isn't valid for this setting.</summary>
    public string? Normalize(string input)
    {
        input = input.Replace("\r", "").Replace("\n", "").Trim();
        switch (Kind)
        {
            case SettingKind.Bool:
                if (input.Equals("true", StringComparison.OrdinalIgnoreCase)) return "true";
                if (input.Equals("false", StringComparison.OrdinalIgnoreCase)) return "false";
                return null;
            case SettingKind.Number:
                return double.TryParse(input, NumberStyles.Float, Inv, out var d) ? FormatNumber(d) : null;
            case SettingKind.Choice:
                if (input == OriginalValue) return input;
                foreach (var o in Options)
                    if (o.Equals(input, StringComparison.OrdinalIgnoreCase)) return o;
                return null;
            default:
                return input;
        }
    }

    public string RenderLine(string value)
    {
        if (Quoted) value = "\"" + value + "\"";
        return Constraint is null ? $"{Key} = {value}" : $"{Key} = {value} // {Constraint}";
    }
}

public sealed class Section
{
    public required string Name { get; init; }
    public List<Setting> Settings { get; } = new();
}

/// <summary>
/// A parsed settings file. Keeps every original line so that saving changes only the
/// values that were edited and nothing else: comments, blank lines, line endings and BOM
/// all come back exactly as they were.
/// </summary>
public sealed class SettingsDocument
{
    static readonly Regex RangeRx = new(@"^(-?\d+(?:\.\d+)?) to (-?\d+(?:\.\d+)?)$", RegexOptions.Compiled);
    static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    public string Path { get; }
    public string NewLine { get; }
    public bool HasBom { get; }
    public IReadOnlyList<string> Lines { get; }
    public IReadOnlyList<Section> Sections { get; }
    public IEnumerable<Setting> AllSettings => Sections.SelectMany(s => s.Settings);

    SettingsDocument(string path, string newLine, bool hasBom, List<string> lines, List<Section> sections)
    {
        Path = path; NewLine = newLine; HasBom = hasBom; Lines = lines; Sections = sections;
    }

    public static SettingsDocument Load(string path) => Parse(path, File.ReadAllBytes(path));

    public static SettingsDocument Parse(string path, byte[] bytes)
    {
        bool bom = bytes.Length >= 3 && bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2];
        string text = Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        string newLine = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(l => l.EndsWith('\r') ? l[..^1] : l).ToList();

        var sections = new List<Section>();
        var current = new Section { Name = "General" };
        var comments = new List<string>();

        for (int i = 0; i < lines.Count; i++)
        {
            string t = lines[i].Trim();
            if (t == "//" && i + 1 < lines.Count && lines[i + 1].TrimStart().StartsWith("// "))
            {
                if (current.Settings.Count > 0) sections.Add(current);
                current = new Section { Name = lines[i + 1].Trim()[2..].Trim() };
                comments.Clear();
                i++;
                continue;
            }
            if (t.StartsWith("//")) { comments.Add(t[2..].Trim()); continue; }
            if (t.Length == 0) { comments.Clear(); continue; }

            // "Key = value"; an empty value is written by the game as "Key = " and by some editors as "Key ="
            int eq = lines[i].IndexOf(" = ", StringComparison.Ordinal);
            if (eq <= 0 && lines[i].EndsWith(" =", StringComparison.Ordinal)) eq = lines[i].Length - 2;
            if (eq <= 0) { comments.Clear(); continue; }

            var setting = ParseSetting(lines[i], i, eq, string.Join(" ", comments));
            comments.Clear();
            if (setting != null) current.Settings.Add(setting);
        }
        if (current.Settings.Count > 0) sections.Add(current);

        return new SettingsDocument(path, newLine, bom, lines, sections);
    }

    static Setting? ParseSetting(string line, int index, int eq, string description)
    {
        string key = line[..eq];
        string rest = line.Length > eq + 3 ? line[(eq + 3)..] : "";
        string? constraint = null;
        int c = rest.IndexOf(" // ", StringComparison.Ordinal);
        string value = rest;
        if (c >= 0) { value = rest[..c]; constraint = rest[(c + 4)..]; }

        // "Fov@0;24892;9411" (current games) or "Fov:0.0" (2022-2024 games): the readable name stops at '@' or ':'.
        int at = key.IndexOfAny(new[] { '@', ':' });
        string name = at > 0 ? key[..at] : key;
        int variant = 0;
        if (at > 0 && key[at] == '@')
        {
            int semi = key.IndexOf(';', at);
            string v = semi > at ? key[(at + 1)..semi] : key[(at + 1)..];
            int.TryParse(v, out variant);
        }

        bool quoted = value.Length >= 2 && value[0] == '"' && value[^1] == '"';
        if (quoted) value = value[1..^1];

        bool isProtected = description.StartsWith("DO NOT MODIFY", StringComparison.OrdinalIgnoreCase);
        var kind = SettingKind.Text;
        double min = 0, max = 0; bool isFloat = false; int decimals = 0;
        IReadOnlyList<string> options = Array.Empty<string>();

        if (constraint != null && RangeRx.Match(constraint) is { Success: true } m)
        {
            kind = SettingKind.Number;
            min = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            max = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            isFloat = m.Groups[1].Value.Contains('.') || m.Groups[2].Value.Contains('.') || value.Contains('.');
            int dot = value.IndexOf('.');
            decimals = dot >= 0 ? value.Length - dot - 1 : (isFloat ? 6 : 0);
        }
        else if (constraint != null && constraint.StartsWith("one of ", StringComparison.Ordinal))
        {
            kind = SettingKind.Choice;
            options = constraint[7..].Split(", ", StringSplitOptions.None);
        }
        else if (constraint == null && (value == "true" || value == "false"))
        {
            kind = SettingKind.Bool;
        }

        return new Setting
        {
            LineIndex = index, Key = key, Name = name, Variant = variant, Description = description,
            OriginalValue = value, Constraint = constraint, Quoted = quoted, Kind = kind, Min = min, Max = max,
            IsFloat = isFloat, Decimals = decimals, Options = options, IsProtected = isProtected,
        };
    }

    /// <summary>The file text with the given settings' values replaced; everything else untouched.</summary>
    public string Render(IReadOnlyDictionary<Setting, string>? newValues = null)
    {
        var lines = Lines.ToArray();
        if (newValues != null)
            foreach (var (setting, value) in newValues)
                lines[setting.LineIndex] = setting.RenderLine(value);
        return string.Join(NewLine, lines);
    }

    public byte[] RenderBytes(IReadOnlyDictionary<Setting, string>? newValues = null)
    {
        var body = Encoding.UTF8.GetBytes(Render(newValues));
        if (!HasBom) return body;
        var all = new byte[body.Length + 3];
        Utf8Bom.CopyTo(all, 0);
        body.CopyTo(all, 3);
        return all;
    }
}
