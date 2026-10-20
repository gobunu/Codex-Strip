# Codex Strip User Guide

[简体中文](使用说明.md) · **English**

Portable packages are available in Chinese and English. Extract the package for your preferred language.

## Getting started

Extract the portable package and run `CodexStrip.exe`. Keep `CodexStrip.exe.config` in the same folder. Codex Strip requires Windows, .NET Framework 4.8, and a running Codex desktop app.

Settings are saved in `data/settings.json` inside the app folder, and wallpaper copies are stored in `data/wallpapers/`. Keep the entire `data/` folder when updating the app.

## Task synchronization

Cards appear in this order: unread completed results, input requests, approval requests, running tasks, then all other states. Errors, capacity limits, reconnecting, pending sync, viewed completions, interrupted, idle, and unloaded tasks share the final group. Each group sorts by most recent interaction, including card clicks. Pinning and manual watches affect discovery, not display priority. Viewing a result or answering a question moves its card to the group matching its updated state.

Under **Settings → Display**, **Recent tasks** selects 10, 20, 30, or 50 recent conversations, plus pinned conversations. The default is 20. **Active window** selects the past 1 hour, 6 hours, 24 hours, 3 days, or any time; the default is 24 hours. These settings are separate from the number of cards displayed.

Each cycle checks the list for changes to timestamps, status, and unread markers. Ended conversations stop detail reads after 30 seconds without changes and resume when they change. Older conversations outside the activity window are skipped rather than read to fill the count. Known running tasks, input or approval requests, and unread changes are still checked. Paused conversations retain their cards and read state. After restarting Strip, older tasks outside the recent list may not appear until the desktop stream discovers them.

Up to four detail reads run at once, and completed reads update their cards immediately. A failed conversation retries later without blocking its peers. List discovery runs in the background with one request at a time and increasing retry delays after failures. Slow lists do not stop known tasks from updating or mark every card as pending sync. Visible ended conversations receive occasional checks while discovery is delayed.

If a turn is too large or details are temporarily unavailable, Strip falls back to a compact snapshot for task status and the latest available reply, while retrying full details at intervals. Missing message text preserves the last content with an unavailable-message label. A card shows pending sync only when neither source can establish its status.

`data/recent-thread-cache.json` stores recent conversation IDs, hosts, titles, status, and timestamps for startup discovery. It stores no message bodies and is never treated as a fresh read receipt. Desktop records with an explicit host can also help discover tasks; Strip no longer guesses across hosts. Some new tasks may still take longer to appear while the list API is unavailable. `data/sync-state.json` records request, retry, and card counts for troubleshooting, without conversation titles, IDs, or messages.

## Idle monitoring

The resource view fades in when entering idle mode and fades out quickly when returning to the task board. If Windows interface animations are disabled, the view switches immediately.

Open **Settings → Idle monitor → Open resource monitor**, or select **Open idle monitor** in the system tray menu. The resource view stays on screen until a new task, a request for input, or an unread result brings the task board back. Click anywhere in the resource view to return to tasks.

The idle timer starts when Codex is connected, tasks are idle, and all items needing attention have been cleared. By default, the resource view opens after 5 minutes. Clicking, pressing a key, or scrolling in the task board resets the timer.

**Settings → Idle monitor → Idle Debug** is off by default. Enable it to show the countdown, blocker count, and Debug entry in the status row; disable it to hide the row and restore the task area. Open **Idle Debug** from this row or **Open idle Debug** in Settings to see each blocking conversation, host, ID, and reason. Reasons include running tasks, reconnecting, input or approval requests, unread results, system errors, and unknown states. Ended failed turns retain their error display but no longer block idle mode merely because they failed. Enter ignore keywords such as `hb, heartbeat` in Debug or Settings → Idle monitor and select Apply. Matching uses case-insensitive title substrings; clear and apply to remove the rules. Rules persist and apply immediately: matching conversations and their completion, unread and sync alerts are hidden from the dashboard and do not affect idle timing or wake. Removing a rule restores visibility without changing read state in Codex. Debug also lists ignored conversations and the matched keyword. Diagnostics include every monitored conversation regardless of card count or host filter, and flag pending sync. Automatic updates do not reset the timer. Conversation details stay in the panel; the local `sleep-state.json` records remaining seconds and counts without titles or IDs.

**Settings → Idle monitor → Ignore conversations by source** offers **Cloud tasks (durable)**, **Local**, **SSH remote**, and **Remote Control**. The same options appear in Debug. Cloud tasks (durable) is selected by default, including when upgrading settings that lack this option. Changes save immediately. Selected sources and their status alerts are hidden immediately and do not block idle mode or wake the resource view. Clear a selection to restore visibility and normal idle behavior. Source rules work alongside title keywords.

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
- Click a task card to open its local, cloud, SSH, or Remote Control conversation in Codex. Temporary child threads open their main thread when a parent link is available. Right-click a card to preview messages. Text and color indicate requests for input, confirmation, and unread results. Completion indicators clear when Codex synchronizes the read state after you view the result on this or another device.
- Click **Weekly left** to open **Account usage**, including the next reset time and available resets. Select **Settings → Display** to change **Task count**, **Host**, **Theme**, and **On top**.
- Open **Settings → Docking** to choose **Floating**, **Top**, **Bottom**, **Left**, or **Right** under **Position**. Choose a **Display** and adjust **Width** or **Height**. The top bar's orientation button switches between horizontal and vertical layouts.
- The minimize button in the upper-right corner sends the app to the system tray. Use **Show / hide dashboard** to restore the window or **Quit Codex Strip** to exit.
