# Codex Strip User Guide

[简体中文](使用说明.md) · **English**

## Getting started

Extract the portable package and run `CodexStrip.exe`. Keep `CodexStrip.exe.config` in the same folder. Codex Strip requires Windows, .NET Framework 4.8, and a running Codex desktop app.

Settings are saved in `data/settings.json` inside the app folder, and wallpaper copies are stored in `data/wallpapers/`. Keep the entire `data/` folder when updating the app.

## Idle monitoring

The resource view fades in when entering idle mode and fades out quickly when returning to the task board. If Windows interface animations are disabled, the view switches immediately.

Open **设置 → 休眠监控 → 立即进入静息** (Settings → Idle monitoring → Enter idle view now), or use the same command in the system tray menu, to open the resource view. After entering manually, resource monitoring stays on screen. A new task, a request for input, or an unread result brings the task board back. Click anywhere in the idle view to return to tasks.

The idle timer starts when Codex is connected, tasks are idle, and all items needing attention have been cleared. By default, the resource view opens after 5 minutes. Clicking, pressing a key, or scrolling in the task board resets the timer.

CPU, GPU, and memory use a single ring by default. Gray shows the remaining capacity, and color shows total usage. Shades of the same color divide usage between the main processes and other processes. Total usage appears in the center; process names and shares appear below the ring. Network and disk figures show average rates over the previous 5 seconds and refresh every 3 seconds. The paired curves below show download/upload and read/write rates over the last 10 seconds.

GPU usage comes from the busiest GPU engine. Process shares use that same engine.

In **设置 → 休眠监控** (Settings → Idle monitoring), enable or disable automatic switching and choose an idle delay of 1, 3, 5, 10, or 30 minutes. Open **自定义项目、壁纸和颜色** (Customize items, wallpaper, and colors) to adjust the displayed items and their order, ring or bar style, smooth or instant updates, network adapter, rate units, accent colors, background image, glass style, and overlay darkness. In smooth mode, rings and curves move gradually toward each sampled value; instant mode updates them directly. Download/read and upload/write use different colors, matched across their labels, dots, and curves.

With a wallpaper set, the monitoring panels use clear glass by default, with rounded highlights and transparency. Select **折射** (Refraction) under **玻璃样式** (Glass style) for stronger distortion at the edges. Adjust **遮罩深度** (Overlay darkness) to lighten or darken the wallpaper.

The selected wallpaper is copied to `data/wallpapers/` and used for the idle view. If the image cannot be read, the default background appears.

CPU process shares come from process CPU time; memory shares come from process working sets; GPU shares come from Windows GPU Engine performance counters. Unavailable counters are displayed as “—”.

## Task board and window

- **设置 → 显示 → 界面倍率** (Settings → Display → Interface scale): choose 100%, 125%, 150%, 175%, or 200%. A new display defaults to 150%. The scale is saved separately for each display and restored when the window moves between them.
- Use the full-screen button in the top bar or `F11` to toggle full screen. Press `Esc` to exit and restore the previous floating position or screen docking.
- Click a task card to open its conversation in Codex. Right-click it to preview messages. Text and color indicate requests for input, confirmation, and unread results. Completion indicators follow Codex's read state and clear after you view the result in Codex.
- Click **周剩余** (Weekly remaining) to view usage limits. Use **设置** (Settings) to change the number of cards, hosts, appearance, always-on-top behavior, and screen docking.
- Dock the window to the top, bottom, left, or right edge of a display, or use a floating window. Adjust the docked window's thickness in Settings. The top bar button switches between horizontal and vertical layouts.
- The collapse button in the upper-right corner sends the app to the system tray. Use the tray menu to show the window again or exit the app.
