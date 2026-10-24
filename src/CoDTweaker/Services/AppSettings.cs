using System.IO;
using System.Text.Json;

namespace CoDTweaker.Services;

/// <summary>The app's own preferences (not the game's).</summary>
public sealed class AppSettings
{
    public bool LockAfterSave { get; set; } = true;
    /// <summary>
    /// Guard mode: files with pinned settings are unlocked while the game runs (so its menu works) and the
    /// pinned values are written back when it exits. The app keeps running in the tray to do that.
    /// </summary>
    public bool GuardPinned { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>A release tag the user chose not to hear about again.</summary>
    public string? DismissedUpdate { get; set; }
    public string? PlayersFolder { get; set; }
    /// <summary>"light", "dark", or null to follow Windows.</summary>
    public string? Theme { get; set; }
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public bool TrayHintShown { get; set; }
    /// <summary>Where the exe last ran from, so other tools (CoD Dedupe) can call it.</summary>
    public string? LastExePath { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.Settings))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.Settings)) ?? new AppSettings();
        }
        catch { /* a damaged settings file just means defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.Settings)!);
            File.WriteAllText(AppPaths.Settings, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* preferences are a convenience; never fail the app over them */ }
    }
}
