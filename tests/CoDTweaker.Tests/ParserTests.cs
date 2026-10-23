using System.IO;
using System.Text;
using CoDTweaker.Models;
using CoDTweaker.ViewModels;
using Xunit;

namespace CoDTweaker.Tests;

public class ParserTests
{
    static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.txt");

    // The fixture is written with LF endings on purpose; normalise in case git touched it.
    static byte[] Fixture() => Encoding.UTF8.GetBytes(File.ReadAllText(FixturePath).Replace("\r\n", "\n"));

    [Fact]
    public void Unchanged_document_renders_byte_for_byte()
    {
        var bytes = Fixture();
        var doc = SettingsDocument.Parse("sample.txt", bytes);
        Assert.Equal(bytes, doc.RenderBytes());
    }

    [Fact]
    public void Crlf_and_bom_are_preserved()
    {
        var text = Encoding.UTF8.GetString(Fixture()).Replace("\n", "\r\n");
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        var doc = SettingsDocument.Parse("sample.txt", bytes);
        Assert.True(doc.HasBom);
        Assert.Equal("\r\n", doc.NewLine);
        Assert.Equal(bytes, doc.RenderBytes());
    }

    [Fact]
    public void Sections_and_kinds_are_detected()
    {
        var doc = SettingsDocument.Parse("sample.txt", Fixture());
        Assert.Equal(new[] { "Audio", "Display", "System" }, doc.Sections.Select(s => s.Name));

        var all = doc.AllSettings.ToDictionary(s => s.Key);
        Assert.Equal(SettingKind.Number, all["Volume@0;14317;21371"].Kind);
        Assert.True(all["Volume@0;14317;21371"].IsFloat);
        Assert.Equal(6, all["Volume@0;14317;21371"].Decimals);
        Assert.Equal(SettingKind.Text, all["VoiceOutputDevice@0;2059;35888"].Kind);
        Assert.Equal("", all["VoiceOutputDevice@0;2059;35888"].OriginalValue);
        Assert.Equal(SettingKind.Bool, all["VoicePushToTalk@0;57752;20945"].Kind);
        Assert.Equal(SettingKind.Choice, all["DisplayMode@0;22564;43096"].Kind);
        Assert.Contains("Fullscreen borderless extended window", all["DisplayMode@0;22564;43096"].Options);
        Assert.Equal(SettingKind.Number, all["MaxFpsInGame@0;13305;58861"].Kind);
        Assert.False(all["MaxFpsInGame@0;13305;58861"].IsFloat);
        Assert.Equal(1, all["DxrMode@1;8707;7764"].Variant);
        Assert.Equal("Sprint Assist Delay Gamepad", all["Sprint Assist Delay Gamepad@1;23176;7764"].Name);
        Assert.True(all["DetectedMemoryAmountMB@0;38495;7764"].IsProtected);
        Assert.False(all["DetectedMemoryAmountMB@0;38495;7764"].HasSlider);
        Assert.True(all["Volume@0;14317;21371"].HasSlider);
        Assert.Equal("Enables DirectX Raytracing", all["DxrMode@0;37334;7764"].Description);
    }

    [Fact]
    public void Only_edited_lines_change()
    {
        var bytes = Fixture();
        var doc = SettingsDocument.Parse("sample.txt", bytes);
        var fps = doc.AllSettings.Single(s => s.Name == "MaxFpsInGame");
        var dxr = doc.AllSettings.Single(s => s.Key.StartsWith("DxrMode@1"));

        var rendered = doc.Render(new Dictionary<Setting, string> { [fps] = "158", [dxr] = "High" });
        var expected = Encoding.UTF8.GetString(bytes)
            .Replace("MaxFpsInGame@0;13305;58861 = 100 // 30 to 300", "MaxFpsInGame@0;13305;58861 = 158 // 30 to 300")
            .Replace("DxrMode@1;8707;7764 = Ultra // one of", "DxrMode@1;8707;7764 = High // one of");
        Assert.Equal(expected, rendered);
    }

    [Fact]
    public void Normalize_validates_and_formats()
    {
        var doc = SettingsDocument.Parse("sample.txt", Fixture());
        var all = doc.AllSettings.Where(s => s.Variant == 0).ToDictionary(s => s.Name);
        all["VolumetricQuality"] = doc.AllSettings.Single(s => s.Name == "VolumetricQuality");

        Assert.Equal("0.500000", all["Volume"].Normalize("0.5"));
        Assert.Equal("1.000000", all["Volume"].Normalize("7"));          // clamped to the range
        Assert.Null(all["Volume"].Normalize("abc"));
        Assert.Equal("300", all["MaxFpsInGame"].Normalize("999"));       // clamped, integer
        Assert.Equal("144", all["MaxFpsInGame"].Normalize("144.4"));
        Assert.Equal("true", all["VoicePushToTalk"].Normalize("TRUE"));
        Assert.Null(all["VoicePushToTalk"].Normalize("yes"));
        Assert.Equal("Windowed", all["DisplayMode"].Normalize("windowed"));
        Assert.Null(all["DisplayMode"].Normalize("Potato"));
        Assert.Equal("QQUALITY_VERYHIGH", all["VolumetricQuality"].Normalize("QQUALITY_VERYHIGH")); // the file's own odd value stays allowed
        Assert.Equal("QUALITY_HIGH", all["VolumetricQuality"].Normalize("QUALITY_HIGH"));
        Assert.Equal("Speakers", all["VoiceOutputDevice"].Normalize("  Speakers \n"));
    }

    [Fact]
    public void ViewModel_tracks_modifications()
    {
        var doc = SettingsDocument.Parse("sample.txt", Fixture());
        var vm = new SettingVm(doc.AllSettings.Single(s => s.Name == "MaxFpsInGame"));
        Assert.False(vm.IsModified);
        vm.NumberValue = 158;
        Assert.True(vm.IsModified);
        Assert.Equal("158", vm.Value);
        vm.Value = "not a number";
        Assert.Equal("158", vm.Value);
        vm.Reset();
        Assert.False(vm.IsModified);

        var choice = new SettingVm(doc.AllSettings.Single(s => s.Name == "VolumetricQuality"));
        Assert.Contains("QQUALITY_VERYHIGH", choice.DisplayOptions);
        choice.Value = "QUALITY_HIGH";
        Assert.DoesNotContain("QQUALITY_VERYHIGH", choice.DisplayOptions);
    }

    [Fact]
    public void Real_settings_files_round_trip_if_present()
    {
        string players = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Activision", "Call of Duty", "players");
        if (!Directory.Exists(players)) return;
        foreach (var f in Directory.EnumerateFiles(players, "*.txt", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(f);
            var doc = SettingsDocument.Parse(f, bytes);
            Assert.Equal(bytes, doc.RenderBytes());
            Assert.NotEmpty(doc.AllSettings);
        }
    }
}
