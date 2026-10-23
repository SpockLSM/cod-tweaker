# CoD Tweaker

Edit Call of Duty's settings files outside the game, including the options the in-game menu doesn't show, and lock them so the game can't overwrite your changes.

![CoD Tweaker](docs/screenshot.png)

Call of Duty keeps its settings in plain text files under `%LOCALAPPDATA%\Activision\Call of Duty\players`. They hold everything the menus offer plus a good deal they don't: frame-generation modes, video-memory share, render worker threads, dynamic-resolution targets, shader-preload behaviour, menu FPS caps and more. Editing them by hand works, but the game rewrites the files when it exits, so edits vanish unless the file is made read-only. CoD Tweaker handles all of that.

## What it does

- **Finds the files** by what's inside them, not by name: the system settings (`s.1.0.cod26.txt` today; the numbers change with builds and games), each player's profile files for every account on the PC, the `options.4.codNN.txt` files of the 2022-2024 games, and any copies you've made yourself.
- **Shows every setting** with the game's own description and limits: sliders for numbers, dropdowns for choices, checkboxes for on/off, text for the rest. Values the game detects itself ("DO NOT MODIFY") are shown but not editable.
- **Search** across names, descriptions and values. **Only changed** shows what you've touched.
- **Saves exactly**: only the values you changed are rewritten. Comments, order, line endings, everything else stays byte for byte as the game wrote it.
- **Backs up before every save** (`%LOCALAPPDATA%\CoDTweaker\backups`), with **Restore backup** to go back.
- **Locks the file after saving** (read-only) so the game can't overwrite your edits. Lock and unlock any time without saving.
- Warns when the game is running, since it only reads these files at startup.

## Use it

1. Download `CoDTweaker.exe` from the [latest release](../../releases/latest) and run it. Nothing to install, no admin rights needed.
2. Pick a file on the left. Change what you like; changed rows get an orange mark and an undo button.
3. Click **Save** (or Ctrl+S). With **Lock the file after saving** ticked, the file becomes read-only.
4. Start the game.

> **Windows SmartScreen** may warn that the app is unrecognised, because it isn't code-signed. Click *More info > Run anyway*. The source is here if you'd rather build it yourself (see below).

### Locked files and the in-game menu

While a settings file is locked, the game can't save to it. On some builds that makes the graphics menu misbehave, up to kicking you out of the match when you open it. If you want to use the in-game settings menu, click **Lock / unlock file** first, make your changes in the game, then lock it again.

### Where to find things

| | |
|---|---|
| Game settings files | `%LOCALAPPDATA%\Activision\Call of Duty\players` (older games: `Documents\Call of Duty\players`) |
| Backups made by this app | `%LOCALAPPDATA%\CoDTweaker\backups` |
| This app's preferences | `%LOCALAPPDATA%\CoDTweaker\settings.json` |

If the game's folder is somewhere unusual, use **Choose folder...**, or start the app with `CoDTweaker.exe --players "D:\path\to\players"`. **Open file...** opens any file in the same format.

## Building it yourself

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build CoDTweaker.slnx -c Release
dotnet test
```

To make the single-file exe that the releases ship:

```
dotnet publish src/CoDTweaker/CoDTweaker.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

Releases are built by GitHub Actions: push a tag such as `v1.2.0` and the exe is attached to a release automatically.

## FAQ

**Can this get me banned?** The app changes the same text files the game's own menu writes, nothing else: no injection, no memory edits, no game files. Still, it isn't made or endorsed by Activision, so use it at your own risk.

**A setting I changed had no effect.** Some values are overridden by the game at startup or depend on others (for example DLSS options only apply when DLSS is the upscaler). Check the file after a session to see what the game kept.

**The game reset everything.** That happens when the game can't read the file or finds a value it doesn't understand. Use **Restore backup** to get your version back.

## Disclaimer

Not affiliated with or endorsed by Activision. Call of Duty is a trademark of Activision Publishing, Inc. Provided as is, without warranty. See [LICENSE](LICENSE).
