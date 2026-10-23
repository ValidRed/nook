# How WinGroups works

## Stack

- **C# + WPF on .NET Framework 4.8**, which ships with Windows 11, so there's nothing extra to install. Built with the framework's own `csc.exe` (see `build.ps1`).
- Windows 11 materials come from DWM: `DWMWA_SYSTEMBACKDROP_TYPE` gives the main window Mica and the group pop-up acrylic (frosted glass); `DWMWA_WINDOW_CORNER_PREFERENCE` rounds the corners; `DWMWA_USE_IMMERSIVE_DARK_MODE` matches dark mode.

## Groups

- Each group is one JSON file in `%LOCALAPPDATA%\WinGroups\groups`: name, widget size (columns × rows, 1–3 each), widget position and apps.
- Start apps (desktop and Store) are stored by their Start ID and launched as `shell:AppsFolder\<id>`.
- Shortcuts picked from the desktop, or dropped on a widget, are **copied** into the group, so the desktop shortcut can be deleted later and the group keeps working. The original is never moved or changed. The copy is written to `%LOCALAPPDATA%\WinGroups\links` when launched.
- Other files (an `.exe`, a document) are stored by path.
- Icons come from `IShellItemImageFactory`, so they match Start and Explorer.

## Widgets

- One borderless, transparent WPF window per group. It's a tool window (`WS_EX_TOOLWINDOW`, not in Alt+Tab or the taskbar) that never activates (`WS_EX_NOACTIVATE`, `WM_MOUSEACTIVATE` → `MA_NOACTIVATE`) and keeps itself at the bottom of the window stack by answering `WM_WINDOWPOSCHANGING` with `HWND_BOTTOM`. That puts it above the wallpaper and desktop icons, below every app.
- Because clicks land on WinGroups' own window, one click is enough. Nothing watches the mouse anywhere else.
- **Grid:** the size of one desktop icon space is read from the desktop's list view (`LVM_GETITEMSPACING`, a read-only query) and converted to device-independent pixels; 75 × 98 by default. A widget is `cols × rows` of those spaces. Dragging moves the window; letting go snaps it to that grid on whichever screen it's on.
- **1×1** draws like a desktop icon (a Start-folder tile of up to four app icons plus the name) and opens the pop-up. **Bigger** widgets lay out as many app slots as fit; if there are more apps, the last slot is "+N" and opens the pop-up.
- New widgets go to the first free grid spot on the main screen, filling columns from the right, since desktop icons fill from the left.
- **Known limit:** *Show desktop* (Win+D) hides widgets along with other windows.

## Pop-up

- Opens beside its widget, inside the screen's work area, and closes when it loses focus (like Start) or after an app launches (a setting).

## Running

- One copy at a time: launching the exe again while it's running hands the request to the running copy over a named pipe.
- `WinGroups.exe` opens the main window and the widgets. `WinGroups.exe --widgets` shows only the widgets; that's what **Start with Windows** runs, through `HKCU\...\Run\WinGroups`, only while that setting is on.
- WinGroups exits when its last window closes: the main window plus every widget. Right-click a widget > **Close widgets** to stop it.

## Theme

- Reads `AppsUseLightTheme` and the accent palette from the registry, and follows changes live (`SystemEvents.UserPreferenceChanged`). Settings can force light or dark.

## Removing

Settings > **Remove WinGroups from this PC** deletes `%LOCALAPPDATA%\WinGroups` and the startup entry. The exe stays where you put it, ready to delete.

## Version 0.1.0

0.1.0 kept groups as `.wingroup` files on the desktop and registered that file type. 0.2.0 moves any such files into the groups folder (old files go to the Recycle Bin) and removes the old registration.

## Not built (yet)

- Keeping widgets visible during *Show desktop* (Win+D).
- Code signing, so SmartScreen and antivirus tools recognise the exe.
