# Desk Groups

Put your apps into groups that live on the Windows 11 desktop. Click a group and it pops open like a Start menu folder; click an app inside to launch it.

## Status

**Design prototype.** There is no installable app yet. The interactive design runs in a Claude design canvas: [Desk Groups Home Base](https://claude.ai/artifact/LDh9dgs5s5tgMQ7byS9YKz) (private to the owner). Its source is versioned in [`design/`](design/).

## What the prototype covers

- **Home** – your groups as Start-folder-style tiles, each with **Edit** and **Remove** (with a confirmation), plus an **Add Group** box.
- **Group editor** – name the group and pick apps from your installed apps and the built-in Windows apps, with search.
- **Desktop** – every group appears on the desktop and pops open like a Start menu folder. Launching an app from a group puts it on the taskbar.
- **Settings** – open groups with one click or a double-click, start with Windows, close a group after opening an app, colors (follow Windows or pick light/dark), group icon style (app grid or folder), show app names, check for updates.
- **Look** – follows the Windows light/dark setting and accent color; rounded surfaces with rise-and-fade motion.

Groups are only ever created by you. The app does not build groups from what is already on the desktop.

## Repository layout

| Path | What it is |
| --- | --- |
| `design/Main.dc.html` | The prototype screen (Claude design canvas format; it renders only inside the canvas). |
| `design/canvas.json` | The canvas layout for the prototype. |
| `docs/real-app-plan.md` | How the real Windows app will work, and the open questions to settle first. |

## Next step

Build the real Windows app described in [`docs/real-app-plan.md`](docs/real-app-plan.md).
