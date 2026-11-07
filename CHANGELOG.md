# Changelog

## 1.0.0 (unreleased)

- First version: sidebar Launcher popup with search, keyboard navigation and running indicators.
- Merges Playnite Software Tools with Launcher's own tools; per-tool category, hide, run as admin and focus-if-open.
- Auto-launch rules: start with Playnite or with matching games (conditions with not/and/or, optional delay),
  graceful-then-forced close on game exit for tools Launcher started.
- Main menu and game context menu entries, including a per-game auto-launch toggle.
- Renamed from "Toolbox Utility". Settings saved as `toolbox.json` are moved to `launcher.json` on first start.
