using System.IO;
using Spock.AppKit;
using Xunit;

namespace Spock.AppKit.Tests;

public class VersionTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.3.0+a1b2c3d", "1.3.0")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("V2.0.1-beta", "2.0.1")]
    public void Parses_tags_and_informational_versions(string text, string expected) =>
        Assert.Equal(new Version(expected), AppVersion.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v")]
    public void Rejects_things_that_are_not_versions(string text) => Assert.Null(AppVersion.Parse(text));

    [Fact]
    public void Release_json_becomes_release_info_and_compares()
    {
        var check = new UpdateCheck("SpockLSM/cod-tweaker", "CoD Tweaker");
        var r = check.Parse("{\"tag_name\":\"v1.4.0\",\"html_url\":\"https://github.com/SpockLSM/cod-tweaker/releases/tag/v1.4.0\"}");
        Assert.NotNull(r);
        Assert.Equal(new Version(1, 4, 0), r!.Version);
        Assert.EndsWith("/tag/v1.4.0", r.Url);
        Assert.True(UpdateCheck.IsNewer(r, new Version(1, 3, 0)));
        Assert.False(UpdateCheck.IsNewer(r, new Version(1, 4, 0)));
        Assert.False(UpdateCheck.IsNewer(r, new Version(2, 0, 0)));
        Assert.Null(check.Parse("{\"tag_name\":\"nightly\"}"));
    }
}

public class CrashGuardTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "SpockAppKitTests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void Writes_a_readable_crash_file_and_keeps_the_newest_twenty()
    {
        string? path = CrashGuard.Write(_dir, "Test App", new InvalidOperationException("boom"), "unit test");
        Assert.NotNull(path);
        string text = File.ReadAllText(path!);
        Assert.Contains("Test App", text);
        Assert.Contains("boom", text);
        Assert.Contains("Where: unit test", text);

        for (int i = 0; i < 25; i++)
        {
            File.WriteAllText(Path.Combine(_dir, $"crash-2000-01-01_00-00-{i:00}.txt"), "old");
            File.SetLastWriteTime(Path.Combine(_dir, $"crash-2000-01-01_00-00-{i:00}.txt"), new DateTime(2000, 1, 1).AddMinutes(i));
        }
        CrashGuard.Write(_dir, "Test App", new Exception("again"), "unit test");
        Assert.Equal(20, Directory.GetFiles(_dir, "crash-*.txt").Length);
    }
}

public class PlacementAndStartupTests
{
    [Theory]
    [InlineData(1180, 760, true)]
    [InlineData(100, 100, false)]   // under the minimum
    [InlineData(9000, 760, false)]  // wider than the desktop
    [InlineData(double.NaN, 760, false)]
    public void A_saved_size_is_used_only_when_it_fits(double w, double h, bool fits) =>
        Assert.Equal(fits, WindowPlacement.Fits(w, h, 960, 600, 3840, 2160));

    [Fact]
    public void Start_with_windows_round_trips_through_the_run_key()
    {
        const string name = "SpockAppKitUnitTest";
        try
        {
            Assert.True(StartupRun.Set(name, @"C:\Some Folder\App.exe", "--tray"));
            Assert.True(StartupRun.IsSet(name));
            Assert.Equal("\"C:\\Some Folder\\App.exe\" --tray", StartupRun.Get(name));
        }
        finally { StartupRun.Clear(name); }
        Assert.False(StartupRun.IsSet(name));
    }

    [Theory]
    [InlineData(512L, "0.5 KB")]
    [InlineData(5L * 1024 * 1024, "5.0 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3.0 GB")]
    public void Sizes_format_in_the_right_unit(long bytes, string expected) => Assert.Equal(expected, SizeConverter.Format(bytes));
}
