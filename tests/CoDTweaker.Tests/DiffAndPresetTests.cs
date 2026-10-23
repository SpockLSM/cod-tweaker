using System.IO;
using System.Text;
using CoDTweaker.Models;
using CoDTweaker.Services;
using CoDTweaker.ViewModels;
using Xunit;

namespace CoDTweaker.Tests;

public class DiffAndPresetTests
{
    static byte[] Fixture() => Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.txt")).Replace("\r\n", "\n"));
    static string FixtureText() => Encoding.UTF8.GetString(Fixture());

    [Fact]
    public void Pair_matches_by_key_first_and_by_name_and_variant_when_the_hash_changed()
    {
        var mine = SettingsDocument.Parse("a.txt", Fixture());
        // The other file is a later build: one key's hash changed, two values differ.
        string otherText = FixtureText()
            .Replace("MaxFpsInGame@0;13305;58861 = 100", "MaxFpsInGame@0;99999;11111 = 144")
            .Replace("DxrMode@1;8707;7764 = Ultra", "DxrMode@1;8707;7764 = Low");
        var other = SettingsDocument.Parse("b.txt", Encoding.UTF8.GetBytes(otherText));

        var r = Diff.Pair(mine, Diff.Refs(other));

        Assert.Equal(mine.AllSettings.Count(), r.Pairs.Count);
        Assert.Empty(r.OnlyInOther);
        Assert.Equal(0, r.OnlyInMine);
        Assert.Equal("144", r.Pairs.Single(p => p.Mine.Name == "MaxFpsInGame").Theirs);
        Assert.Equal("Low", r.Pairs.Single(p => p.Mine.Name == "DxrMode" && p.Mine.Variant == 1).Theirs);
        Assert.Equal("On", r.Pairs.Single(p => p.Mine.Name == "DxrMode" && p.Mine.Variant == 0).Theirs);
    }

    [Fact]
    public void Values_with_no_home_are_reported_not_dropped()
    {
        var mine = SettingsDocument.Parse("a.txt", Fixture());
        var theirs = new[] { new ValueRef(null, "MaxFpsInGame", 0, "90"), new ValueRef(null, "NotAThing", 0, "1"), new ValueRef(null, "MaxFpsInGame", 0, "91") };
        var r = Diff.Pair(mine, theirs);
        Assert.Single(r.Pairs);
        Assert.Equal(2, r.OnlyInOther.Count); // the unknown name and the second claim on the same setting
        Assert.Equal(mine.AllSettings.Count() - 1, r.OnlyInMine);
    }

    [Fact]
    public void Preset_round_trips_and_applies_across_renamed_keys()
    {
        var doc = SettingsDocument.Parse("s.1.0.cod26.txt", Fixture());
        var fps = doc.Match("MaxFpsInGame", 0)!;
        var ptt = doc.Match("VoicePushToTalk", 0)!;
        var preset = Preset.From("cod26", "system", "System settings (cod26)", new[] { (fps, "158"), (ptt, "false") });
        preset.Values.Add(new PresetValue { Name = "NoSuchSetting", Variant = 0, Value = "1" });

        var back = Preset.FromJson(preset.ToJson());
        Assert.Equal("cod26", back.Game);
        Assert.Equal("system", back.Kind);
        Assert.Equal(3, back.Values.Count);
        Assert.Equal(fps.Key, back.Values[0].Key);

        var later = SettingsDocument.Parse("s.1.0.cod27.txt",
            Encoding.UTF8.GetBytes(FixtureText().Replace("MaxFpsInGame@0;13305;58861", "MaxFpsInGame@0;1;2")));
        var (matched, unmatched) = back.MatchAgainst(later);
        Assert.Equal(2, matched.Count);
        Assert.Equal("158", matched.Single(m => m.Setting.Name == "MaxFpsInGame").Value);
        Assert.Equal("false", matched.Single(m => m.Setting.Name == "VoicePushToTalk").Value);
        Assert.Single(unmatched);
        Assert.Equal("NoSuchSetting", unmatched[0].Name);
    }

    [Fact]
    public void Preset_values_are_normalised_and_protected_settings_are_skipped()
    {
        var doc = SettingsDocument.Parse("a.txt", Fixture());
        var preset = Preset.From("cod26", "system", "t", new[] { (doc.Match("MaxFpsInGame", 0)!, "1000"), (doc.Match("DxrMode", 0)!, "off") });
        var (matched, _) = preset.MatchAgainst(doc);
        Assert.Equal("300", matched.Single(m => m.Setting.Name == "MaxFpsInGame").Value); // clamped to the range
        Assert.Equal("Off", matched.Single(m => m.Setting.Name == "DxrMode").Value);        // the game's spelling
        var detected = doc.AllSettings.FirstOrDefault(s => s.IsProtected);
        if (detected != null)
        {
            var p2 = Preset.From("cod26", "system", "t", new[] { (detected, "1") });
            var (m2, u2) = p2.MatchAgainst(doc);
            Assert.Empty(m2);
            Assert.Single(u2);
        }
    }

    [Fact]
    public void Broken_presets_are_refused()
    {
        Assert.ThrowsAny<Exception>(() => Preset.FromJson("{\"values\":[]}"));
        Assert.ThrowsAny<Exception>(() => Preset.FromJson("{\"values\":[{\"value\":\"1\"}]}"));
        Assert.ThrowsAny<Exception>(() => Preset.FromJson("not json at all"));
    }

    [Fact]
    public void Compare_view_model_lists_differences_and_take_makes_pending_edits()
    {
        var doc = SettingsDocument.Parse("a.txt", Fixture());
        var sections = doc.Sections.Select(s => new SectionVm(s)).ToList();
        var cvm = new CompareViewModel(doc, sections.SelectMany(s => s.All), new List<BackupInfo>());
        var preset = Preset.From("cod26", "system", "t", new[] { (doc.Match("MaxFpsInGame", 0)!, "144"), (doc.Match("DisplayMode", 0)!, "Fullscreen") });

        cvm.UsePreset(preset, "the test preset");

        Assert.Single(cvm.Rows);
        Assert.Equal("MaxFpsInGame", cvm.Rows[0].Mine.Model.Name);
        Assert.Equal("Display", cvm.Rows[0].Section);
        Assert.Contains("1 difference", cvm.Summary);
        Assert.Contains("1 matching", cvm.Summary);
        Assert.True(cvm.TakeAllCommand.CanExecute(null));

        cvm.Rows[0].Take();

        Assert.Equal("144", cvm.Rows[0].Mine.Value);
        Assert.False(cvm.Rows[0].IsDifferent);
        Assert.True(cvm.Rows[0].Mine.IsModified);
        Assert.Equal("✓ Same", cvm.Rows[0].TakeText);
    }
}
