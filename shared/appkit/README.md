# Spock.AppKit

The plumbing shared by [CoD Tweaker](https://github.com/SpockLSM/cod-tweaker) and [CoD Dedupe](https://github.com/SpockLSM/cod-dedupe): one copy instead of two drifting ones.

| Piece | What it does |
|---|---|
| `Theme` + `Themes/*.xaml` | Light and dark brush sets, the control styles (buttons, inputs, a themed ComboBox, menus, list views), a dark title bar, and a one-call switch that repaints every open window. |
| `TrayIcon` | A notification-area icon on `Shell_NotifyIcon` with a WPF context menu and balloon notices. No Windows Forms. Uses the exe's own icon. |
| `CrashGuard` | Turns an unhandled exception into a `crash-*.txt` the user can send along plus a plain message, instead of Windows' bare error box. |
| `UpdateCheck` + `AppVersion` | Asks GitHub for the latest release and says whether it is newer than the running build. |
| `WindowPlacement` | Remembers a window's size, and ignores a saved size that no longer fits the screen. |
| `StartupRun` | "Start with Windows" through the current user's Run key. |
| `CliLog` | Console plus log-file output for a window app's command-line mode. |
| `Shell` | Open a folder or a web page; render a window to PNG for the docs. |
| `Mvvm` | `ObservableObject` and `RelayCommand`. |
| `Converters` | Bool-to-visibility, inverse bool, byte sizes. |

## Using it in an app

The apps vendor this repository with `git subtree`, so a plain clone of an app builds with nothing else to fetch:

```text
git subtree add --prefix shared/appkit https://github.com/SpockLSM/appkit.git main --squash
```

Then reference `shared/appkit/src/Spock.AppKit/Spock.AppKit.csproj` from the app project, merge the theme dictionaries in `App.xaml`:

```xml
<ResourceDictionary.MergedDictionaries>
    <ResourceDictionary Source="pack://application:,,,/Spock.AppKit;component/Themes/Light.xaml"/>
    <ResourceDictionary Source="pack://application:,,,/Spock.AppKit;component/Themes/Controls.xaml"/>
</ResourceDictionary.MergedDictionaries>
```

and call `Theme.Apply(...)` before the first window opens. To pick up a newer kit later:

```text
git subtree pull --prefix shared/appkit https://github.com/SpockLSM/appkit.git main --squash
```

## Building

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```text
dotnet build Spock.AppKit.slnx -c Release
dotnet test
```

## License

MIT, see [LICENSE](LICENSE).
