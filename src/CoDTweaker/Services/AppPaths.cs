using System.IO;

namespace CoDTweaker.Services;

/// <summary>Where this app keeps its own files. Tests point the root somewhere disposable.</summary>
public static class AppPaths
{
    public static string? RootOverride { get; set; }

    public static string Root => RootOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoDTweaker");

    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Pins => Path.Combine(Root, "pins.json");
    public static string Logs => Path.Combine(Root, "logs");
    /// <summary>A notes.json here is read on top of the built-in one, so notes can be added without a new build.</summary>
    public static string NotesOverride => Path.Combine(Root, "notes.json");
}
