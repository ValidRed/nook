# How WinGroups works

## Stack

- **C# + WPF on .NET Framework 4.8**, which ships with Windows 11, so there's nothing extra to install. Built with the framework's own `csc.exe` (see `build.ps1`).
- Windows 11 materials come from DWM: `DWMWA_SYSTEMBACKDROP_TYPE` gives the main window Mica and the group pop-up acrylic (frosted glass); `DWMWA_WINDOW_CORNER_PREFERENCE` rounds the corners; `DWMWA_USE_IMMERSIVE_DARK_MODE` matches dark mode.

## Groups on the desktop

Each group is one file on the desktop, for example `Games.wingroup`.

- The `.wingroup` type is registered per user under `HKCU\Software\Classes`, with no admin rights needed. Registration happens when WinGroups starts and is refreshed if the exe has moved.
- `DefaultIcon` is `%1`, so Explorer draws the icon stored **inside the file**: a Start-folder-style tile of the first four apps, with no shortcut arrow.
- `NeverShowExt` hides the extension, so the desktop just says "Games". Renaming the file renames the group.
- The file is an `.ico` followed by the group's JSON (name comes from the file name; apps, order). Copying, moving or deleting the file copies, moves or deletes the group.
- Double-clicking runs `WinGroups.exe open "<file>"`. Dropping files onto the icon adds them (Windows' standard drop handler passes them as extra arguments).
- When a group changes, WinGroups rewrites the file and tells Explorer with `SHUpdateImage` and `SHChangeNotify(SHCNE_UPDATEITEM)` so the desktop redraws it.

## Apps inside groups

- Start apps (desktop and Store) are stored by their Start ID and launched as `shell:AppsFolder\<id>`.
- Shortcuts picked from the desktop (or dropped on a group) are **copied** into the group, so you can later delete the desktop shortcut and the group keeps working. The original is never moved or changed. The copy is written to `%LOCALAPPDATA%\WinGroups\links` when launched.
- Other files (an `.exe`, a document) are stored by path.
- Icons come from `IShellItemImageFactory`, so they match Start and Explorer.

## Pop-up

- Opens beside the group you double-clicked, inside the screen's work area, and closes when it loses focus (like Start) or after an app launches (a setting).
- One copy of WinGroups at a time: double-clicking a second group while one is open hands the request to the open copy over a named pipe.
- WinGroups exits when its last window closes. Nothing runs in the background and nothing is added to startup.

## Theme

- Reads `AppsUseLightTheme` and the accent palette from the registry, and follows changes live (`SystemEvents.UserPreferenceChanged`).
- Desktop icons are redrawn when Windows switches between light and dark.

## Removing

Settings > **Remove WinGroups from this PC** moves the group files to the Recycle Bin, removes the file type registration and deletes `%LOCALAPPDATA%\WinGroups`. The exe stays where you put it, ready to delete.

## Not built (yet)

- Opening groups with a single click. Explorer only does that when Windows itself is set to single-click.
- Code signing, so SmartScreen and antivirus tools recognise the exe.
