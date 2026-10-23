using System.IO;
using System.Text;
using CoDTweaker.Models;
using CoDTweaker.Services;
using Xunit;

namespace CoDTweaker.Tests;

public class FileLocatorTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "CoDTweakerTests", Guid.NewGuid().ToString("N"));
    readonly string _sample;

    // The format the 2022-2024 games wrote: ':' in the key and quoted values.
    const string OldStyle = "//\n// Audio\n\n// Adjusts overall volume of the game\nVolume:0.0 = \"1.000000\" // 0.000000 to 1.000000\n\n// Fullscreen mode\nDisplayMode:0.0 = \"Fullscreen\" // one of Windowed, Fullscreen\n";

    public FileLocatorTests()
    {
        _sample = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.txt")).Replace("\r\n", "\n");
        Directory.CreateDirectory(Path.Combine(_root, "1234567890123456"));
        Directory.CreateDirectory(Path.Combine(_root, "shaders"));
        Put("s.1.0.cod26.txt", _sample);
        Put("s.2.3.cod27.txt", _sample);                        // a later build's numbers
        Put("options.4.cod23.txt", OldStyle);                    // an older game
        Put("s.1.0.cod26.txt_bak.txt", _sample);                 // the player's own copy
        Put("s.1.0.cod26.txt.before-review-2026.txt", _sample);  // another copy
        Put("something_renamed.txt", _sample);                   // right format, unknown name
        Put("notes.txt", "my loadout ideas\nnot a settings file\n");
        Put(Path.Combine("1234567890123456", "g.p.cod26.1.0.l.txt"), _sample);
        Put(Path.Combine("1234567890123456", "g.p.cod26.pm.1.0.l.txt"), _sample);
        Put(Path.Combine("shaders", "cache.txt"), "binaryish\x00stuff");
    }

    void Put(string rel, string text) => File.WriteAllBytes(Path.Combine(_root, rel), Encoding.UTF8.GetBytes(text));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Recognises_files_by_content_and_names_them_from_the_pattern()
    {
        var files = FileLocator.Find(_root);
        var byName = files.ToDictionary(f => Path.GetFileName(f.Path));

        Assert.Equal("System settings (cod26)", byName["s.1.0.cod26.txt"].Title);
        Assert.Equal("System settings (cod27)", byName["s.2.3.cod27.txt"].Title);
        Assert.Equal("Options (cod23)", byName["options.4.cod23.txt"].Title);
        Assert.Equal("Player profile (cod26)", byName["g.p.cod26.1.0.l.txt"].Title);
        Assert.Equal("Player profile (cod26, pm)", byName["g.p.cod26.pm.1.0.l.txt"].Title);
        Assert.Equal("Player 1234567890123456", byName["g.p.cod26.1.0.l.txt"].Group);
        Assert.Equal("something_renamed.txt", byName["something_renamed.txt"].Title);
        Assert.Equal("Game settings", byName["something_renamed.txt"].Group);
        Assert.Equal("Other copies", byName["s.1.0.cod26.txt_bak.txt"].Group);
        Assert.Equal("Other copies", byName["s.1.0.cod26.txt.before-review-2026.txt"].Group);
        Assert.DoesNotContain("notes.txt", byName.Keys);
        Assert.DoesNotContain("cache.txt", byName.Keys);
        Assert.StartsWith("Sections: Audio, Display, System", byName["s.1.0.cod26.txt"].Subtitle);

        // Real files first, copies last.
        Assert.Equal("Other copies", files[^1].Group);
        Assert.Equal("Game settings", files[0].Group);
    }

    [Fact]
    public void Old_style_files_parse_and_round_trip()
    {
        var bytes = Encoding.UTF8.GetBytes(OldStyle);
        var doc = SettingsDocument.Parse("options.4.cod23.txt", bytes);
        Assert.Equal(bytes, doc.RenderBytes());

        var vol = doc.AllSettings.Single(s => s.Name == "Volume");
        Assert.Equal(SettingKind.Number, vol.Kind);
        Assert.Equal("1.000000", vol.OriginalValue);
        Assert.True(vol.Quoted);
        Assert.Equal("Volume:0.0 = \"0.500000\" // 0.000000 to 1.000000", vol.RenderLine("0.500000"));

        var mode = doc.AllSettings.Single(s => s.Name == "DisplayMode");
        Assert.Equal(SettingKind.Choice, mode.Kind);
        Assert.Equal("Fullscreen", mode.OriginalValue);
    }

    [Fact]
    public void Display_path_hides_the_user_profile()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.Equal(@"%LOCALAPPDATA%\Activision\Call of Duty\players", FileLocator.DisplayPath(Path.Combine(local, "Activision", "Call of Duty", "players")));
        Assert.Equal(@"D:\Games\players", FileLocator.DisplayPath(@"D:\Games\players"));
    }
}
