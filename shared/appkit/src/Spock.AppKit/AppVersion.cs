using System.Reflection;

namespace Spock.AppKit;

/// <summary>The running app's version, as the release tag names it.</summary>
public static class AppVersion
{
    /// <summary>The informational version, e.g. "1.3.0+a1b2c3d" when built from a git checkout.</summary>
    public static string Informational
    {
        get
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? asm.GetName().Version?.ToString() ?? "0.0.0";
        }
    }

    /// <summary>Major.minor.patch, with any "+commit" suffix dropped.</summary>
    public static Version Current => Parse(Informational) ?? new Version(0, 0, 0);

    /// <summary>"v1.2.3", "1.2.3+abc" and "1.2" all parse; anything else is null.</summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().TrimStart('v', 'V');
        int plus = text.IndexOfAny(new[] { '+', '-' });
        if (plus >= 0) text = text[..plus];
        if (!Version.TryParse(text, out var v)) return null;
        return new Version(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
    }

    public static string Short => Current.ToString(3);
}
