# Real app plan

How the prototype in `design/` turns into a Windows 11 app. Nothing here is built yet.

## Stack

- **C# + WPF on .NET Framework 4.8.** It ships with Windows 11, so users install nothing extra, and its libraries are precompiled, which keeps start-up fast.
- Windows 11 visuals come from DWM: `DWMWA_SYSTEMBACKDROP_TYPE` for Mica/acrylic, `DWMWA_WINDOW_CORNER_PREFERENCE` for rounded corners, `DWMWA_USE_IMMERSIVE_DARK_MODE` for the frame.

## Groups on the desktop

Each group is a small file on the desktop, for example `Games.deskgroup`.

- The `.deskgroup` type is registered per user under `HKCU\Software\Classes` (no admin rights needed).
- `DefaultIcon` is `%1`, so Explorer draws the icon stored **inside the file**. That gives each group its own app-grid icon with **no shortcut arrow**.
- `NeverShowExt` hides the extension, so the desktop shows just "Games". Renaming the file renames the group.
- The group's data (name, apps, order) is stored after the icon data in the same file. Copying, moving or deleting the file copies, moves or deletes the group, and it survives OneDrive sync.
- The `open` verb runs `DeskGroups.exe open "%1"`, which shows the pop-up next to the icon.

Checked on the owner's PC (2026-10-22): a custom extension with `DefaultIcon=%1` shows the icon embedded in the file, and data after the icon doesn't break it.

## Pop-up

- A borderless WPF window with acrylic, rounded corners and the system shadow. It unfolds from the icon the way a Start menu folder does, and closes when it loses focus or after an app launches (a setting).
- Apps launch through `ShellExecute`. Store apps launch as `shell:AppsFolder\<AppUserModelID>`.
- The app stays running in the background so pop-ups open instantly. "Start with Windows" uses the `HKCU\...\Run` key.

## App picker

- Lists everything in `shell:AppsFolder` (desktop and Store apps), split into installed apps and Windows apps.
- Icons come from `IShellItemImageFactory`, so they match what Start shows.

## Theme

- Reads `AppsUseLightTheme` and the accent color, and listens for `WM_SETTINGCHANGE` to follow changes live.
- Settings can override this with Light or Dark.

## Open questions

1. **One-click opening.** Explorer opens desktop files on double-click unless Windows itself is set to single-click. Opening a group with one click while Windows uses double-click needs a different technique (for example watching clicks on the desktop), so it needs a decision before it's built.
2. **Icon refresh.** When a group's apps change, Explorer may keep showing the old icon from its cache. Plan: `SHChangeNotify(SHCNE_UPDATEITEM)` plus `SHUpdateImage`, and test early. In a quick test, a fresh process saw the new icon but an already running one kept the old one.
3. **Dropping apps onto a desktop group** to add them (a shell `DropHandler`). Nice to have; later.

## Uninstall

Removes the per-user file type registration and the Run key. Group files on the desktop stay until the user deletes them.
