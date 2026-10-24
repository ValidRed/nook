# Nook

Everything on your Windows 11 desktop has its own place: app groups as frosted-glass widgets, one-click workspaces, an island at the top of the screen, and a quiet desktop clock.

## Try it

1. Download `Nook.exe` from the [Releases page](https://github.com/ValidRed/nook/releases).
2. Put it somewhere permanent, for example `Documents\Nook\`. (If you turn on **Start with Windows**, it starts from wherever the exe is.)
3. Run it. The exe isn't signed yet, so Windows may show "Windows protected your PC": choose **More info** > **Run anyway**.
4. The first time, a short **Welcome to Nook** window asks two things: open Nook when Windows starts, and add a Nook shortcut to your desktop. Both are off until you turn them on, and both can be changed later on Home. (If Nook.exe is still in Downloads or a temporary folder, it says so, since the startup entry and shortcut point at wherever the exe is.)
5. Nook opens on **Home**, with tabs along the top (Home, Groups, Workspaces, Island, Clock), a short "How to use Nook" guide you can close and bring back, and your settings. When a new version is out, a small note in the corner offers **Update now** or **Later**.
6. In **Groups**, click **Add Group**, name it, choose a size and color, pick apps, and click **Create group**. Its widget appears on your desktop.
7. Click the widget. A **1×1** widget looks like a desktop icon and pops open in frosted glass; bigger widgets show the apps directly, so one click launches one.

Nook was called WinGroups before version 0.7. The first time Nook runs, it brings over your WinGroups groups and settings.

## Using widgets

- **Move:** drag a widget. It lifts, a dashed outline shows where it will land, and it glides into place on the grid. Workspace buttons move the same way.
- **Rearrange apps:** drag an app within a widget, or onto another widget to move it there. In the editor, drag apps in **In this group**.
- **Add apps:** drop shortcuts onto a widget. Your original shortcuts are never moved, changed or deleted.
- **See every app in a full widget:** when a group has more apps than its widget can show, click **+N** or the name at the top (it lights up on hover). Otherwise the name is just a label.
- **Right-click a widget** to open it, edit the group, lock widgets, open Nook, or quit Nook.
- **Show desktop (Win+D)** keeps your widgets visible.
- **Quick search:** press **Ctrl + Alt + Space** anywhere to search your groups and installed apps; Enter opens.

## Widget sizes and colors

Sizes are squares as wide as a desktop icon column (75 px at 100% scale), from 1×1 up to 3×3. In a group's editor pick **1×1**, **2×2** or **3×3**, or type any **Width** and **Length** from 1 to 3.

Each group has its own color: **Default** (follows light/dark), the **accent color**, **match the wallpaper** behind it, a preset, or any `#RRGGBB`. You can also hide a widget's name or show icons only.

## Settings (on Home)

- **Mode** (light/dark or follow Windows) and **accent color** (Nook blue to start; match Windows, a preset, or any `#RRGGBB`). The dark look uses the same deep blues as the website.
- **Window look:** Nook's own window in solid colors, or frosted glass that blurs what's behind it, with its own **Window glass blur** slider.
- **Widget glass blur:** how frosted widgets and pop-ups are, from clear glass to heavy frost.
- Everything is lettered in Helvetica Bold. Windows doesn't include Helvetica, so Arial (its close cousin) stands in until it's installed.
- **Start with Windows:** shows only your widgets and the island at sign-in. Off unless you turn it on.
- **Shortcut on your desktop:** add or remove a Nook shortcut (`Nook.lnk`). Nook never replaces a `Nook.lnk` that isn't its own.
- **Lock widgets**, **quick search**, **close a group after opening an app**, **1×1 style**, **app names**.
- **Backup:** export your groups to a file and import them on another PC.

## Workspaces

A workspace is a one-click button on your desktop that opens a set of apps and lays them out on a screen. Make one in Nook > **Workspaces**.

- **Button:** pick an icon (work, code, games, music...) and a tint. The name is optional; without one the button is just its icon.
- **Size:** 1×1 is just the icon; 2×2 also shows a small map of the layout with each app's icon where it goes.
- **Layout:** just open, full screen, side by side, main and side, three columns, main and two, or four corners. The numbers on the apps in **In this workspace** are the spaces they fill; drag to change which app goes where.
- **Screen:** the main screen or any other.
- All the apps open at once, and each window is put in its place as soon as it appears (and put back if it moves itself right after). Two copies of the same app open one after the other, so they land in the order you set.
- Apps that are already open are moved into place instead of opened again. Nook only looks at windows for the few seconds after you click.

## The island

Turn it on in Nook > **Island**. A small pill sits at the top center of the screen you pick (or every screen) and never takes focus from your apps.

- **Time**, always. Click the island to open it; scroll while it's open to flip between pages:
  - **Music** (while something plays): the song, a progress bar and play/pause/skip; click the cover to open the player.
  - **Timer:** pick a length with − and +, or tap 1, 5, 10, 25 or 45 min to start right away. While it runs you can pause it, add a minute or stop it.
  - **Shelf:** what you've dropped on the island (see below).
  - **Time:** the time, date and (on laptops) the battery.
  - **System:** CPU, memory, GPU and network, with small graphs. Only read while this page is open.
- **While resting:** album art and a visualizer that moves with the sound; a new song shows for a moment on its own.
- **A running timer is a live activity:** its countdown and an orange ring sit on the resting island (next to the time, and the music if it's playing), and clicking the island opens the timer. When time's up, the island opens to say so, with **Restart** and **Stop**, and Windows' alarm sound plays for up to 20 seconds (turn it off with **Timer sound**). Every island shows the same timer.
- **Shelf:** drag files, links, text or pictures onto the island and drop them to keep them there while you get to where they go; then open the island and drag them off into a folder, an email or a document. The island opens into a drop zone as soon as something is dragged over it, and the resting island shows how many things are waiting.
  - Your files stay where they are: the shelf only points at them, and dragging one off copies it (or makes a shortcut), so your original never moves.
  - Pictures and files that a program hands over without a file on disk (a picture from a browser, a mail attachment) get a copy in Nook's folder, which goes once you drag it off or take it off the shelf.
  - Click a file to open it, a link to open it in your browser, or text to copy it. **×** takes one thing off, **Clear** takes everything off. Up to 10 things fit, and they're still there after a restart.
- **Volume:** scroll over the resting island.
- **Headphones:** shows them for a moment when they connect, with their battery when Bluetooth headphones report it.
- **Charging** (laptops): the battery shows for a moment when you plug in or unplug.
- **Look:** solid black, or frosted glass with its own blur slider, either one in any tint you like (light colors are deepened a little so the white text stays readable). Floating, or a notch that hangs from the top edge, as wide as you like when it's resting.
- It hides while a full-screen game, video or presentation is in front.

What's playing comes from the media controls Windows already shows in its volume flyout. The visualizer reads the level meter Windows keeps for your speakers (how loud, never the sound itself). Nothing is recorded.

## Desktop clock

A big clock on the screens you pick (one, several, or all), in Nook > **Clock**. It sits behind your windows like the widgets, and clicks pass straight through it. A **size** slider sets how big the time is (48 to 240 px); choose 12- or 24-hour time, and seconds if you like.

- **Font:** pick from a box of fonts, each shown in its own lettering: Arial, Times New Roman and Helvetica to start. **+** adds a font file (.ttf, .otf or .ttc), copied into Nook's folder so it works without installing it; **×** on a font removes it.
- **Look:** solid digits, or frosted glass that shows a blur of your wallpaper through each digit, with its own **Glass blur** slider.
- **Weight** from thin to bold, and a **color** (white or dark picked to suit your wallpaper, a color taken from your wallpaper, a preset, or any color); on glass, the color tints the glass, and a **light sweep** moves across it now and then.
- **Like a lock screen:** the date above the time (or under it, or hidden), the clock in the upper third of the screen (or centered), **taller numbers**, and a **glance row** under the time with what's playing (or "Nothing playing") and, on laptops, the battery.

## Updates

Nook looks for a newer version on the GitHub releases page a little after it starts, then twice a day (turn this off in **Home > Updates**); opening Nook's window checks again if it's been a while. When there is one, a small note greets you when Nook's window opens (**Update now** or **Later**), and **Home > Updates**, a widget's right-click menu and the island's menu offer **Update**. One click downloads it next to your `Nook.exe`, checks it against GitHub's size and checksum, swaps it in and restarts.

## What it changes on your PC

| Change | Where |
| --- | --- |
| Your groups, settings, private copies of shortcuts you put in groups, fonts you add for the clock, and what's on the island's shelf (with copies of pictures and files that came without one on disk) | `%LOCALAPPDATA%\Nook` |
| Only if you turn on **Start with Windows**: a startup entry that shows your widgets at sign-in | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Nook` |
| Only if you ask for it: a shortcut to Nook on your desktop | `Desktop\Nook.lnk` |
| Only while you update: `Nook.update.exe` and `Nook.old.exe` next to `Nook.exe`, removed as soon as the new version starts | The folder `Nook.exe` is in |

Nook doesn't install or copy itself anywhere, doesn't watch your mouse or keyboard (quick search uses a standard Windows shortcut registration), and never touches your desktop icons or wallpaper. It runs while its widgets, the island or the clock are showing; right-click a widget or the island > **Quit Nook** to stop it.

**To remove it:** Nook > **Home** > **Remove Nook from this PC** (it also takes away the startup entry and Nook's desktop shortcut), then delete `Nook.exe`.

## Build

Needs only Windows 10/11 (it uses the C# compiler that ships with .NET Framework 4.8):

```powershell
.\build.ps1
```

The exe lands in `dist\`. Developer checks that don't change anything:

```powershell
dist\Nook.exe --dev-selftest report.txt   # app list, groups round-trip, snapping on every screen
dist\Nook.exe --dev-render out\           # renders every page, widgets on your wallpaper, search and an intro frame
dist\Nook.exe --dev-site site\assets      # refreshes the website's icons and preview card
dist\Nook.exe --dev-island out\           # every island state (black, glass, notch) and the clock, with made-up song details
dist\Nook.exe --dev-media report.txt      # checks what's playing, the level meter, headphones, system stats and the update check (no song details)
dist\Nook.exe --dev-workspace report.txt  # opens three blank test windows as a workspace (one jumps away by itself), checks each lands in its space, closes them
dist\Nook.exe --dev-drag report.txt [big|group]  # drags a test workspace button (or a 2x2 one, or a group) with the real pointer; checks it moved without launching anything
dist\Nook.exe --dev-popup-live out\       # opens a real group pop-up over a test card and checks the window is exactly the size of its glass
dist\Nook.exe --dev-home-live out\        # opens Nook's window the way the app does, visits every tab, checks the tab highlight, the titles' colors and the update note
dist\Nook.exe --dev-setup out\            # the first-run window with its choices off and on, then closed: checks nothing was turned on
dist\Nook.exe --dev-timer report.txt      # a short timer on the real island: counts down on the resting island, opens when time's up, stops (no sound)
dist\Nook.exe --dev-shelf out\            # with the real pointer: drags text, a test file and a picture onto the island, then two of them off onto a test window
dist\Nook.exe --dev-clock-live out\       # shows the real desktop clock, solid then frosted glass, and saves what it draws
dist\Nook.exe --dev-islands report.txt    # puts the island on every screen at a set width, then back to one screen
```

## Repository layout

| Path | What it is |
| --- | --- |
| `src/` | The app (C#, WPF, XAML). |
| `assets/Nook.ico` | The app icon. |
| `design/` | The original clickable design (Claude design canvas format). |
| `docs/real-app-plan.md` | How the app works under the hood. |
| `site/` | The website: one static page (`index.html`, `styles.css`, `main.js`) plus `assets/`. Published to [GitHub Pages](https://validred.github.io/nook/) from the `gh-pages` branch: `git subtree push --prefix site origin gh-pages`. |
| `release.ps1` | Publishes the version in `src\Program.cs` to GitHub (with a `WinGroups.exe` copy for older updaters), then points the website's download buttons at it and republishes the site. |
