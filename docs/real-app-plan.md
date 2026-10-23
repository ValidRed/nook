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

- One borderless, transparent WPF window per group. It's a tool window (`WS_EX_TOOLWINDOW`, not in Alt+Tab or the taskbar) that never activates (`WS_EX_NOACTIVATE`, `WM_MOUSEACTIVATE` → `MA_NOACTIVATE`).
- **Z-order:** every `WM_WINDOWPOSCHANGING` places the widget directly above the desktop window (the root of the desktop's `SHELLDLL_DefView`). Normally that's the bottom of the stack, above wallpaper and icons and below every app. When Win+D raises the desktop over your apps, a check every 400 ms notices the widget is no longer right above it and restacks it, so widgets stay visible.
- **Frosted glass:** Windows only blurs behind active windows and widgets never activate, so the wallpaper (read with `IDesktopWallpaper`, per monitor, honoring fill/fit/stretch/span) is blurred once and each widget shows the piece behind it as an `ImageBrush`. The blur strength setting re-blurs it; wallpaper changes are picked up within 20 seconds.
- **Dragging:** the widget lifts (scale 1.04), a click-through dashed outline shows the snap target, and on release it glides there. Dragging an app slot starts an internal drag-and-drop instead, to reorder within the widget or move the app to another widget. **Lock widgets** turns dragging off.
- Because clicks land on WinGroups' own window, one click is enough. Nothing watches the mouse anywhere else.
- **Grid:** the desktop icon spacing is read from the desktop's list view (`LVM_GETITEMSPACING`, a read-only query) and converted to device-independent pixels (75 × 98 by default). Widgets use square units as wide as one icon column, so a widget is `cols × rows` squares and equal sizes come out square. Dragging moves the window; letting go snaps it to that square grid on whichever screen it's on.
- **Colors:** each group stores its own widget color: empty for the theme default, `accent` for the Windows accent (follows changes live), or `#RRGGBB`. Text and hover colors are picked from the background's brightness.
- **Title:** bigger widgets show the group name centered at the top inside a pill that lights up on hover; clicking it opens the pop-up with every app.
- **1×1** draws like a desktop icon (a Start-folder tile of up to four app icons plus the name) and opens the pop-up. **Bigger** widgets lay out as many app slots as fit; if there are more apps, the last slot is "+N" and opens the pop-up.
- New widgets go to the first free grid spot on the main screen, filling columns from the right, since desktop icons fill from the left.
- **Undo:** deleting a group moves its file to `groups.trash`; Undo moves it back within 7 seconds, otherwise it's deleted.

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

- Code signing, so SmartScreen and antivirus tools recognise the exe.

## Main window motion

- Mouse-wheel scrolling is eased: each notch sets a target and the page glides there, frame-rate independent (`CompositionTarget.Rendering`).
- The first time the window opens, blue fluid fills the window and drains away (two layers of gently moving waves, accelerating like a drain). As the surface passes the logo, its four blue squares pop up out of it with a few droplets; the logo then glides into the title-bar icon while the page rises in. Skipped when Windows animations are turned off.
- The window reopens at the size and position you left it.

## Quick search and accent

- **Ctrl+Alt+Space** is registered with `RegisterHotKey` on a message-only window (nothing watches the keyboard). It opens a frosted search window that lists matching apps from your groups first, then installed apps.
- A custom accent color is expanded into the same seven light/dark shades Windows uses (by adjusting HSL lightness); "Match Windows" reads the system palette.
