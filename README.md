# Launcher Utility

A [Playnite](https://playnite.link) extension that puts all your utilities, such as save editors, trainers, controller tools and OBS,
**one click away** in the sidebar, and can start and stop them automatically with your games.

## Features

- **Sidebar button → Launcher popup.** A compact, searchable list of your tools. Type to filter, press ↑/↓ and Enter, or just click.
  Each tool opens in its own window.
- **Uses what you already have.** Tools from *Library → Software Tools* appear automatically, alongside tools you add in
  Launcher settings. Software Tools can be given a category, hidden, or set to run as admin, without changing them in Playnite.
- **No duplicates.** If a tool is already open, clicking it brings its window to the front. A green dot shows which tools are running.
- **Run as administrator** for individual tools (UAC prompt).
- **Auto-launch rules** (inspired by [LaunchMate](https://github.com/ASchoe311/LaunchMate)):
  - start a tool when Playnite starts, and/or when a matching game starts (with an optional delay);
  - match games by specific game, name, platform, source, tag, category, genre or feature, with *not*, *and* and *or*;
  - close the tool when the game exits. It asks the tool to close politely first (so OBS can stop recording cleanly) and forces it
    only after a timeout. **Only tools Launcher started for that game are closed.** Anything you already had open is left alone.
- **Menus:** *Extensions → Launcher Utility* lists every tool. A game's right-click menu has *Launch ▸* and
  *Auto-launch with this game ▸* (a quick toggle that adds or removes that game from the tool's rule).

## Install

Download the `.pext` from the releases page and open it, or drag it onto Playnite. Requires Playnite 10 (SDK 6.17+), Desktop mode.

## Notes and limitations

- Playnite's SDK does not expose Software Tools. Launcher reads them from Playnite's internal database by reflection. If a future
  Playnite update changes that, Launcher shows a notice and keeps working with the tools added in its own settings.
- Script-type Software Tools are listed but cannot be launched from the Launcher yet.
- A tool started *as administrator* cannot be closed by a non-elevated Playnite. Launcher shows a notification instead.
- Tools that hand off to another process (launcher stubs, some shortcuts) cannot be tracked for "close on exit".
- English only for now.

## Build

```powershell
dotnet build LauncherUtility.slnx -c Release
dotnet test LauncherUtility.slnx -c Release
./build/pack.ps1            # validates metadata, builds and writes artifacts/*.pext
```

Layout: `src/LauncherUtility.Core` holds the Playnite-free logic (catalog merge, search, rules, auto-launch coordinator, persistence)
and is unit-tested in `tests/`. `src/LauncherUtility` is the Playnite plugin, Windows process handling and the WPF UI.
Settings are stored in `launcher.json` in the extension's data folder.
