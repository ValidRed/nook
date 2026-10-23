# WinGroups

Put your apps into groups that live on the Windows 11 desktop. Double-click a group and it pops open in frosted glass, like a Start menu folder; click an app inside to launch it.

## Try it

1. Download `WinGroups.exe` from the [Releases page](https://github.com/ValidRed/desk-groups/releases).
2. Move it somewhere permanent first, for example `Documents\WinGroups\`. Groups on the desktop open with whichever copy of WinGroups last ran, so don't leave it in Downloads if you plan to clear that folder.
3. Run it. The exe isn't signed yet, so Windows may show "Windows protected your PC". Choose **More info** > **Run anyway**.
4. Click **Add Group**, name it, pick apps, and click **Create group**. The group appears on your desktop.
5. Double-click the group on the desktop to open it. Click outside it (or press Esc) to close it.

You can also drop app shortcuts onto a group's desktop icon to add them. Your original shortcuts are never moved, changed or deleted.

## What it changes on your PC

| Change | Where |
| --- | --- |
| Your groups, one small file each (`Games.wingroup`, shown as "Games") | Your Desktop |
| The group file type, so Explorer can draw and open groups | `HKCU\Software\Classes\.wingroup` and `HKCU\Software\Classes\WinGroups.Group` |
| Settings and private copies of shortcuts you put in groups | `%LOCALAPPDATA%\WinGroups` |

WinGroups doesn't install itself anywhere, doesn't run at startup, doesn't run in the background, and doesn't touch your other desktop icons. It closes when its last window closes.

**To remove it:** open WinGroups > menu > **Settings** > **Remove WinGroups from this PC**. That moves your groups to the Recycle Bin and undoes the changes above. Then delete `WinGroups.exe`.

## Features

- **Home** – your groups as Start-folder-style tiles with **Edit** and **Remove** (with a confirmation), plus an **Add Group** box.
- **Group editor** – name the group and pick from your desktop shortcuts, installed apps and Windows apps (with search), or **Browse** for any app.
- **Group pop-up** – frosted-glass (acrylic) panel with each app's real icon. Right-click an app for **Run as administrator** or **Remove from group**.
- **Settings** – close a group after opening an app, colors (follow Windows or pick light/dark), group icon style (app grid or folder), show app names.
- **Look** – Mica window, follows the Windows light/dark setting and accent color, rounded surfaces with rise-and-fade motion.

Groups are only ever created by you; WinGroups never builds groups from what's already on your desktop.

## Build

Needs only Windows 10/11 (it uses the C# compiler that ships with .NET Framework 4.8):

```powershell
.\build.ps1
```

The exe lands in `dist\`. Developer checks that don't install anything:

```powershell
dist\WinGroups.exe --dev-selftest report.txt   # reads your app list, round-trips a group file in a temp folder
dist\WinGroups.exe --dev-render out\           # renders every screen to PNG, light and dark
```

## Repository layout

| Path | What it is |
| --- | --- |
| `src/` | The app (C#, WPF, XAML). |
| `assets/WinGroups.ico` | The app icon. |
| `design/` | The original clickable design (Claude design canvas format). |
| `docs/real-app-plan.md` | How the app works under the hood. |
