# WinGroups

Put your apps into groups that live on the Windows 11 desktop as frosted-glass widgets. One click opens a group or launches an app.

## Try it

1. Download `WinGroups.exe` from the [Releases page](https://github.com/ValidRed/desk-groups/releases).
2. Put it somewhere permanent, for example `Documents\WinGroups\`. (If you turn on **Start with Windows**, it starts from wherever the exe is.)
3. Run it. The exe isn't signed yet, so Windows may show "Windows protected your PC": choose **More info** > **Run anyway**.
4. Click **Add Group**, name it, choose a size and color, pick apps, and click **Create group**. Its widget appears on your desktop.
5. Click the widget. A **1×1** widget looks like a desktop icon and pops open in frosted glass; bigger widgets show the apps directly, so one click launches one.

## Using widgets

- **Move:** drag a widget. It lifts, a dashed outline shows where it will land, and it glides into place on the grid.
- **Rearrange apps:** drag an app within a widget, or onto another widget to move it there. In the editor, drag apps in **In this group**.
- **Add apps:** drop shortcuts onto a widget. Your original shortcuts are never moved, changed or deleted.
- **Open a bigger widget's group:** click the name at the top (it lights up on hover).
- **Right-click a widget** to open it, edit the group, lock widgets, open WinGroups, or quit WinGroups.
- **Show desktop (Win+D)** keeps your widgets visible.
- **Quick search:** press **Ctrl + Alt + Space** anywhere to search your groups and installed apps; Enter opens.

## Widget sizes and colors

Sizes are squares as wide as a desktop icon column (75 px at 100% scale), from 1×1 up to 3×3. In a group's editor pick **1×1**, **2×2** or **3×3**, or type any **Width** and **Length** from 1 to 3.

Each group has its own color: **Default** (follows light/dark), the **accent color**, **match the wallpaper** behind it, a preset, or any `#RRGGBB`. You can also hide a widget's name or show icons only.

## Settings

- **Mode** (light/dark or follow Windows) and **accent color** (match Windows, a preset, or any `#RRGGBB`).
- **Glass blur:** how frosted widgets and pop-ups are, from clear glass to heavy frost.
- **Start with Windows:** shows only your widgets and the island at sign-in.
- **Lock widgets**, **quick search**, **close a group after opening an app**, **1×1 style**, **app names**.
- **Backup:** export your groups to a file and import them on another PC.

## Workspaces

A workspace is a one-click button on your desktop that opens a set of apps and lays them out on a screen. Make one in WinGroups > menu > **Workspaces**.

- **Button:** pick an icon (work, code, games, music...) and a color. The name is optional; without one the button is just its icon.
- **Layout:** just open, full screen, side by side, main and side, three columns, main and two, or four corners. The numbers on the apps in **In this workspace** are the spaces they fill; drag to change which app goes where.
- **Screen:** the main screen or any other.
- Apps that are already open are moved into place instead of opened again. WinGroups only looks at windows for the few seconds after you click.

## The island

Turn it on in WinGroups > menu > **Island**. A small pill sits at the top center of a screen you pick and never takes focus from your apps.

- **Time**, always. Click the island to open it; scroll while it's open to flip between pages:
  - **Music** (while something plays): the song, a progress bar and play/pause/skip; click the cover to open the player.
  - **Time:** the time, date and (on laptops) the battery.
  - **System:** CPU, memory, GPU and network, with small graphs. Only read while this page is open.
- **While resting:** album art and a visualizer that moves with the sound; a new song shows for a moment on its own.
- **Volume:** scroll over the resting island.
- **Headphones:** shows them for a moment when they connect, with their battery when Bluetooth headphones report it.
- **Charging** (laptops): the battery shows for a moment when you plug in or unplug.
- **Look:** solid black, or frosted glass with its own blur slider. Floating, or a notch that hangs from the top edge.
- It hides while a full-screen game, video or presentation is in front.

What's playing comes from the media controls Windows already shows in its volume flyout. The visualizer reads the level meter Windows keeps for your speakers (how loud, never the sound itself). Nothing is recorded.

## Desktop clock

A big, quiet clock centered on the screens you pick (one, several, or all), in WinGroups > menu > **Clock**. It sits behind your windows like the widgets, picks white or dark text to suit your wallpaper, and clicks pass straight through it. Choose small, medium or large, with or without the date.

## Updates

WinGroups looks for a newer version on the GitHub releases page a little after it starts, then twice a day (turn this off in **Settings > Updates**). When there is one, **Settings > Updates**, a widget's right-click menu and the island's menu offer **Update**. One click downloads it next to your `WinGroups.exe`, checks it against GitHub's size and checksum, swaps it in and restarts. This needs the releases to be public.

## What it changes on your PC

| Change | Where |
| --- | --- |
| Your groups, settings, and private copies of shortcuts you put in groups | `%LOCALAPPDATA%\WinGroups` |
| Only if you turn on **Start with Windows**: a startup entry that shows your widgets at sign-in | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WinGroups` |
| Only while you update: `WinGroups.update.exe` and `WinGroups.old.exe` next to `WinGroups.exe`, removed as soon as the new version starts | The folder `WinGroups.exe` is in |

WinGroups doesn't install itself, doesn't watch your mouse or keyboard (quick search uses a standard Windows shortcut registration), and never touches your desktop icons or wallpaper. It runs while its widgets or the island are showing; right-click either > **Quit WinGroups** to stop it.

**To remove it:** WinGroups > menu > **Settings** > **Remove WinGroups from this PC**, then delete `WinGroups.exe`.

## Build

Needs only Windows 10/11 (it uses the C# compiler that ships with .NET Framework 4.8):

```powershell
.\build.ps1
```

The exe lands in `dist\`. Developer checks that don't change anything:

```powershell
dist\WinGroups.exe --dev-selftest report.txt   # app list, groups round-trip, snapping on every screen
dist\WinGroups.exe --dev-render out\           # renders screens, widgets on your wallpaper, search and an intro frame
dist\WinGroups.exe --dev-island out\           # every island state (black, glass, notch) with made-up song details
dist\WinGroups.exe --dev-media report.txt      # checks what's playing, the level meter, headphones, system stats and the update check (no song details)
dist\WinGroups.exe --dev-workspace report.txt  # opens two blank test windows as a workspace, checks they land side by side, closes them
```

## Repository layout

| Path | What it is |
| --- | --- |
| `src/` | The app (C#, WPF, XAML). |
| `assets/WinGroups.ico` | The app icon. |
| `design/` | The original clickable design (Claude design canvas format). |
| `docs/real-app-plan.md` | How the app works under the hood. |
