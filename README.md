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

## The island

Turn it on in WinGroups > menu > **Island**. A small pill sits at the top center of your main screen and never takes focus from your apps.

- **Time**, always. Click the island for the date (and the battery on laptops).
- **Music:** while something plays (Spotify, Apple Music, a browser...), the album art and a visualizer that moves with the sound. Click for the song, a progress bar and play/pause/skip; click the cover to open the player. A new song shows for a moment on its own.
- **Volume:** scroll over the island.
- **Charging** (laptops): the battery shows for a moment when you plug in or unplug.
- **Look:** solid black, or frosted glass with its own blur slider. Floating, or a notch that hangs from the top edge.
- It hides while a full-screen game, video or presentation is in front.

What's playing comes from the media controls Windows already shows in its volume flyout. The visualizer reads the level meter Windows keeps for your speakers (how loud, never the sound itself). Nothing is recorded.

## What it changes on your PC

| Change | Where |
| --- | --- |
| Your groups, settings, and private copies of shortcuts you put in groups | `%LOCALAPPDATA%\WinGroups` |
| Only if you turn on **Start with Windows**: a startup entry that shows your widgets at sign-in | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WinGroups` |

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
dist\WinGroups.exe --dev-media report.txt      # checks that what's playing and the level meter can be read (yes/no and numbers only)
```

## Repository layout

| Path | What it is |
| --- | --- |
| `src/` | The app (C#, WPF, XAML). |
| `assets/WinGroups.ico` | The app icon. |
| `design/` | The original clickable design (Claude design canvas format). |
| `docs/real-app-plan.md` | How the app works under the hood. |
