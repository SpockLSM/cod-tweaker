using System.IO;
using System.Text.Json;

namespace CoDTweaker.Services;

/// <summary>The app's own preferences (not the game's).</summary>
public sealed class AppSettings
{
    public bool LockAfterSave { get; set; } = true;
    public string? PlayersFolder { get; set; }
    /// <summary>"light", "dark", or null to follow Windows.</summary>
    public string? Theme { get; set; }
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }

    static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoDTweaker", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* a damaged settings file just means defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* preferences are a convenience; never fail the app over them */ }
    }
}
