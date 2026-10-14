# Codex Strip User Guide

[简体中文](使用说明.md) · **English**

Portable packages are available in Chinese and English. Extract the package for your preferred language.

## Getting started

Extract the portable package and run `CodexStrip.exe`. Keep `CodexStrip.exe.config` in the same folder. Codex Strip requires Windows, .NET Framework 4.8, and a running Codex desktop app.

Settings are saved in `data/settings.json` inside the app folder, and wallpaper copies are stored in `data/wallpapers/`. Keep the entire `data/` folder when updating the app.

## Idle monitoring

The resource view fades in when entering idle mode and fades out quickly when returning to the task board. If Windows interface animations are disabled, the view switches immediately.

Open **Settings → Idle monitor → Open resource monitor**, or select **Open idle monitor** in the system tray menu. The resource view stays on screen until a new task, a request for input, or an unread result brings the task board back. Click anywhere in the resource view to return to tasks.

The idle timer starts when Codex is connected, tasks are idle, and all items needing attention have been cleared. By default, the resource view opens after 5 minutes. Clicking, pressing a key, or scrolling in the task board resets the timer.

The status row shows an idle countdown updated every second, or the number of conversations blocking idle mode. Open **Idle Debug** from this row or **Settings → Idle monitor** to see each blocking conversation, host, ID, and reason. Reasons include running tasks, reconnecting, input or approval requests, unread results, errors, and unknown states. Enter ignore keywords such as `hb, heartbeat` in Debug or Settings → Idle monitor and select Apply. Matching uses case-insensitive title substrings; clear and apply to remove the rules. Rules persist and affect idle timing and wake only; task cards and unread indicators still appear. Debug also lists ignored conversations and the matched keyword. Diagnostics include every monitored conversation regardless of card count or host filter, and flag pending sync. Automatic updates do not reset the timer. Conversation details stay in the panel; the local `sleep-state.json` records remaining seconds and counts without titles or IDs.

CPU, GPU, and memory use a single ring by default. Gray shows the remaining capacity, and color shows total usage. Shades of the same color divide usage between the main processes and other processes. Total usage appears in the center; process names and shares appear below the ring. Network and disk figures show average rates over the previous 5 seconds and refresh every 3 seconds. The paired curves below show download/upload and read/write rates over the last 10 seconds.

GPU usage comes from the busiest GPU engine. Process shares use that same engine.

In **Settings → Idle monitor**, set **Auto idle** to **On** or **Off** and choose an **Idle delay** of 1, 3, 5, 10, or 30 minutes. Open **Customize monitor** for the following options:

| Setting | Options |
| :--- | :--- |
| **Glass style** | **Clear** preserves the wallpaper; **Refraction** adds distortion at the edges |
| **Charts** | **Rings · By app** or **Bars** |
| **Animation** | **Smooth** moves gradually toward each reading; **Instant** updates directly |
| **Metrics** | Choose CPU, GPU, Memory, Network, and Disk; use ↑ and ↓ to change their order |
| **Rate units** | **Auto**, **MB/s**, or **Mb/s** |
| **Network adapter** | **Auto** or a specific adapter |
| **Accent** | **Blue**, **Green**, **Orange**, or **Purple** |
| **Wallpaper** | **Choose image…** to set a background; **Clear** to restore the default |
| **Overlay** | Adjust the wallpaper's shading |

Download/read and upload/write use different colors, matched across their labels, dots, and curves. With a wallpaper set, monitoring panels use clear glass by default, with rounded highlights and transparency. Wallpaper copies are saved in `data/wallpapers/`.

CPU process shares come from process CPU time; memory shares come from process working sets; GPU shares come from Windows GPU Engine performance counters.

## Task board and window

- **Settings → Display → UI scale**: choose 100%, 125%, 150%, 175%, or 200%. A new display defaults to 150%. Each display keeps its own scale, restored when the window moves between them.
- Use the full-screen button in the top bar or `F11` to toggle full screen. Press `Esc` to exit and restore the previous floating position or screen docking.
- Click a task card to open its conversation in Codex. Right-click it to preview messages. Text and color indicate requests for input, confirmation, and unread results. Completion indicators follow Codex's read state and clear after you view the result in Codex.
- Click **Weekly left** to open **Account usage**, including the next reset time and available resets. Select **Settings → Display** to change **Task count**, **Host**, **Theme**, and **On top**.
- Open **Settings → Docking** to choose **Floating**, **Top**, **Bottom**, **Left**, or **Right** under **Position**. Choose a **Display** and adjust **Width** or **Height**. The top bar's orientation button switches between horizontal and vertical layouts.
- The minimize button in the upper-right corner sends the app to the system tray. Use **Show / hide dashboard** to restore the window or **Quit Codex Strip** to exit.
