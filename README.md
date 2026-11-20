# Copixy

A small, fast screenshot tool for Windows 10/11 with annotations, a precision magnifier and pixel-exact area selection.

- One portable file — `Copixy.exe` (~620 KB), no installer.
- Runs on .NET Framework 4.8, which is built into Windows 10/11.
- Lives in the system tray; no network access, no telemetry.

> Copixy was previously called Screenshoter. On first launch it migrates the old settings and the autostart entry automatically.

## Getting started
Run `Copixy.exe` — it goes to the system tray. Take a screenshot by:
- pressing the hotkey (**PrtSc** by default, configurable in Settings);
- clicking the tray icon;
- launching the exe again (desktop shortcut, pinned taskbar icon).

> On Windows 11, PrtSc is often taken by the Snipping Tool. If Copixy shows a warning on startup, turn off
> *Settings → Accessibility → Keyboard → Use the Print screen key to open screen capture*, or pick another hotkey.

## Selecting an area
Capture starts in Move mode: select an area, then copy or save it right away.
- Drag to draw a frame with handles on the corners and sides.
- Resize by any corner, handle or side; drag inside the frame to move it.
- **Shift** — square. **Alt** — grow from the center. **Shift+Alt** — square from the center.
  Works both when creating the frame and when resizing by a corner.
- Click without dragging — select the whole monitor.
- Exact size — width and height fields on the toolbar: click, type a number, press Enter (Tab switches fields, ↑/↓ — ±1).
- **Magnifier** for precise aiming: a circle (×6, 300 px by default) around the cursor.
  Hold **Z** — release to hide it. Or click the magnifier button on the toolbar (right of the eraser) — it stays on until you right-click or press Esc; the current tool keeps working.
- **Space + drag** — move the frame with any tool active.
- Arrow keys — move the frame by 1 px (10 px with Shift); Ctrl+arrows — resize.

## Toolbar
Sticks to the bottom-right corner of the frame (Copy is the rightmost button). If it doesn't fit on screen it jumps
above the frame and/or to the left; if there's no room above or below, it goes inside the frame.

| Action | Key |
|---|---|
| Move — back to moving the frame | M (or V) |
| Move the frame with any tool | hold Space + drag |
| Drawing tools: click the icon for the last used tool; chevron ▾ or long-press for the menu (can be shown inline — see Settings). The eraser is always on the toolbar | — |
| Line / Pencil / Rectangle / Ellipse / Text | L / P / R / O / T |
| Dashed — toggle for line, rectangle and ellipse | D |
| Arrowhead — toggle for line | A |
| Cursor — drop a cursor image (S 24 / M 40 / L 64 px); drag to move, × to delete | C |
| Eraser — erases drawings, the screenshot underneath stays intact | E |
| Erase all — remove all drawings at once (in eraser options) | Delete |
| Magnifier | hold Z, or toolbar button |
| Color: 8 presets + any color via the picker | — |
| Line thickness (shared by all shapes), eraser size | mouse wheel |
| Opacity 10–100 % (lines, pencil, shapes, text) | 1…9 = 10…90 %, 0 = 100 % |
| Blend mode: Normal, Multiply, Screen, Overlay, Soft light, Darken, Lighten, Difference | toolbar menu |
| Text size and weight (Light → Bold) | mouse wheel |
| Undo / Redo | Ctrl+Z / Ctrl+Y |
| Copy to clipboard | Ctrl+C, Enter, double-click |
| Save (PNG or JPG) | Ctrl+S; Shift+click or Ctrl+Shift+S — without the dialog |
| Settings | — |
| Close | Esc, right-click |

While drawing, **Shift** snaps lines to 45° and makes squares/circles; **Alt** draws shapes from the center.
To move the frame again, press Move (M), click the active tool again, or right-click.

**Text:** click inside the frame and type; Enter — new line; Esc, Ctrl+Enter or a click elsewhere — done.
Text stays an object: drag it (in Text, Cursor or Move mode), click it to edit, use × or Delete to remove it.

## Settings
Open from the tray menu (*Settings…*) or the gear button on the toolbar.
- Start with Windows.
- Minimize to the tray on startup (if off, Settings open on startup).
- Hide drawing tools under a chevron, or show them inline.
- Hotkey.
- Save folder, default format (PNG/JPG), JPG quality.
- File name: base name + number / date / time, next number, live example.
- Text font (bundled Inter by default, or any installed font).
- Magnifier: zoom (2–16×) and diameter (100–800 px).

Settings are stored in `%APPDATA%\Copixy\settings.ini`.

## Building from source
Run `build.cmd`. It builds `Copixy.exe` with the C# compiler that ships with Windows (`csc.exe` from .NET Framework) — no Visual Studio needed.

Embedded resources: icons from `resources\icons` (except `app_*`), cursor images from `resources\cursors`,
fonts from `resources\fonts`, and the app icon `resources\app.ico`.

Icons come in sets per button: `name_xSm.png` (16), `_Sm` (20), `_Md` (24), `_Lg` (32), `_xLg` (40), `_2xLg` (48).
The base toolbar icon size is 24 px at 100 % scaling; for other display scales the closest pre-rendered size is used.

## Licenses
- Toolbar icons — custom set (`resources\icons`, sizes xSm…2xLg = 16…48 px).
- Font: [Inter](https://rsms.me/inter) — SIL Open Font License 1.1 (embedded, Latin + Cyrillic subset).
