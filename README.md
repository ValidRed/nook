# WinGroups

Put your apps into groups that live on the Windows 11 desktop as widgets. One click opens a group or launches an app.

## Try it

1. Download `WinGroups.exe` from the [Releases page](https://github.com/ValidRed/desk-groups/releases).
2. Put it somewhere permanent, for example `Documents\WinGroups\`. (If you turn on **Start with Windows**, it starts from wherever the exe is.)
3. Run it. The exe isn't signed yet, so Windows may show "Windows protected your PC": choose **More info** > **Run anyway**.
4. Click **Add Group**, name it, choose a widget size, pick apps, and click **Create group**. Its widget appears on your desktop.
5. Click the widget. A **1×1** widget looks like a desktop icon and pops open in frosted glass; bigger widgets show the apps directly, so one click launches one.

**Moving widgets:** drag one anywhere; it snaps to the desktop icon grid when you let go.
**Adding apps:** drop shortcuts onto a widget. Your original shortcuts are never moved, changed or deleted.
**Right-click a widget** to open it, edit the group, open WinGroups, or close the widgets.

## Widget sizes

Sizes are measured in desktop icon spaces, the same grid your desktop icons use, from 1×1 up to 3×3. In a group's editor, pick **1×1**, **2×2** or **3×3**, or type any **Width** and **Length** from 1 to 3 (for example 3×1 for a strip of apps).

## What it changes on your PC

| Change | Where |
| --- | --- |
| Your groups, settings, and private copies of shortcuts you put in groups | `%LOCALAPPDATA%\WinGroups` |
| Only if you turn on **Start with Windows**: a startup entry that shows your widgets at sign-in | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WinGroups` |

WinGroups doesn't install itself, doesn't watch your mouse or keyboard, and never touches your desktop icons. It runs while its widgets are on the desktop; right-click a widget > **Close widgets** to stop it.

**To remove it:** WinGroups > menu > **Settings** > **Remove WinGroups from this PC**, then delete `WinGroups.exe`.

**Coming from 0.1.0?** Groups you made then (files on the desktop) are moved into WinGroups automatically and the old files go to the Recycle Bin. The old `.wingroup` file type is unregistered.

## Features

- **Home** – your groups as Start-folder-style tiles with **Edit** and **Remove** (with a confirmation), plus an **Add Group** box.
- **Group editor** – name, widget size, and apps from your desktop shortcuts, installed apps and Windows apps (with search), or **Browse** for any app.
- **Widgets** – sit above the wallpaper and below your windows, never take focus, snap to the desktop grid.
- **Pop-up** – frosted-glass (acrylic) panel with each app's real icon. Right-click an app for **Run as administrator** or **Remove from group**.
- **Settings** – start with Windows (widgets only), close a group after opening an app, colors (follow Windows or pick light/dark), 1×1 widget style (app grid or folder), show app names.
- **Look** – Mica window, follows the Windows light/dark setting and accent color, rounded surfaces with rise-and-fade motion.

Groups are only ever created by you; WinGroups never builds groups from what's already on your desktop.

## Build

Needs only Windows 10/11 (it uses the C# compiler that ships with .NET Framework 4.8):

```powershell
.\build.ps1
```

The exe lands in `dist\`. Developer checks that don't change anything:

```powershell
dist\WinGroups.exe --dev-selftest report.txt   # reads your app list, round-trips groups in a temp folder
dist\WinGroups.exe --dev-render out\           # renders every screen and widget size to PNG, light and dark
```

## Repository layout

| Path | What it is |
| --- | --- |
| `src/` | The app (C#, WPF, XAML). |
| `assets/WinGroups.ico` | The app icon. |
| `design/` | The original clickable design (Claude design canvas format). |
| `docs/real-app-plan.md` | How the app works under the hood. |
