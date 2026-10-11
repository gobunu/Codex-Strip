<h1 align="center"><img src="assets/icon.png" alt="" width="36" height="36"> Codex Strip</h1>

<p align="center"><a href="README.md">简体中文</a> · <strong>English</strong></p>

A task status bar for Codex on Windows, with horizontal and vertical layouts for secondary displays. Shows task progress, requests for input, and usage limits. When idle, it can switch to system resource monitoring.

[Download portable version](https://github.com/gobunu/Codex-Strip/releases/latest) · [User guide (Chinese)](使用说明.md) · [Report an issue](https://github.com/gobunu/Codex-Strip/issues)

## Screenshots

### Task board

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="screenshots/tasks-dark.png">
  <source media="(prefers-color-scheme: light)" srcset="screenshots/tasks-light.png">
  <img src="screenshots/tasks-light.png" alt="Task board showing hosts, task titles, latest progress, status, and remaining weekly usage" width="100%">
</picture>

Displays each task's host, status, latest message, and remaining weekly usage. Completion indicators follow Codex's read state. The screenshots use sample data and show both light and dark themes.

### Resource monitor

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="screenshots/resources-dark.png">
  <source media="(prefers-color-scheme: light)" srcset="screenshots/resources.png">
  <img src="screenshots/resources.png" alt="Idle resource view showing CPU, GPU, and memory usage, plus network and disk speed charts" width="100%">
</picture>

Shows CPU, GPU, and memory usage, plus network and disk speeds. When no tasks are running or awaiting input, it can open automatically after a configurable delay. New tasks, questions, or unread results bring the task board back.

## Features

| Feature | Details |
| :--- | :--- |
| Tasks | Progress, requests for input, and unread results for local, cloud, and SSH tasks |
| Navigation | Click to open a conversation; temporary child threads open their main thread when a parent link is available |
| Usage limits | Remaining weekly usage and limit details |
| Resource monitor | CPU, GPU, and memory usage, usage by major processes, and network and disk speed charts |
| Layout | Horizontal or vertical, docking to any screen edge, floating window, fullscreen, and system tray |
| Display settings | 100%–200% scaling, with position and scale saved separately for each monitor |
| Appearance | Light and dark themes, custom wallpapers, clear glass and edge refraction effects |

## Quick start

**Requirements:** Windows, .NET Framework 4.8, and a running Codex desktop app.

1. Download `CodexStrip-portable.zip` from the [latest release](https://github.com/gobunu/Codex-Strip/releases/latest) and extract it.
2. Open the Codex desktop app, then run `CodexStrip.exe`. Keep `CodexStrip.exe.config` in the same folder.
3. Choose your monitor, dock position, number of visible tasks, and interface scale in Settings.

> To update, exit Strip before replacing the application files. Keep the entire `data/` folder.

### Controls

| Action | Result |
| :--- | :--- |
| Click / right-click a task card | Open the conversation / preview its messages |
| Click the remaining weekly usage indicator | View usage limit details |
| `F11` / `Esc` | Toggle fullscreen / exit fullscreen and restore the previous position |
| Click the idle resource view | Return to the task board |
| Click the top bar's minimize button | Minimize to the system tray |

## Development and build

Run in Windows PowerShell:

```powershell
./source/build.ps1
./CodexStrip.exe --self-test
./CodexStrip.exe --settings-test
./source/package.ps1
```

`source/tests/` contains development checks for reading the desktop API, conversation navigation, temporary-to-main thread links, settings, layout, idle behavior, and resource display. Some tests interact with windows or temporarily reserve part of the screen's work area.

## Files

| Path | Contents |
| :--- | :--- |
| `source/` | C# source, build scripts, and development checks |
| `assets/`, `screenshots/` | Icons and example screenshots |
| [使用说明.md](使用说明.md) | Detailed features, configuration, and known limitations (Chinese) |
| [RELEASE_NOTES.md](RELEASE_NOTES.md) | Version changes |
