# Caffeine

A lightweight Windows system tray application that keeps your screen awake.

> No more wiggling your mouse during long presentations, downloads, or remote sessions.

<!-- TODO: Add screenshots here -->

## Features

- **Keep Awake** — Prevents Windows from sleeping or turning off the display using the native `SetThreadExecutionState` API
- **Stay Green Mode** — Alternative method that jiggles the mouse by 1 pixel (keeps collaboration apps showing you as "Available")
- **Auto-Off Timers** — Set caffeine to automatically deactivate after 15m, 30m, 1h, or 2h
- **Pomodoro Timer** — Built-in Pomodoro technique timer with configurable work/break durations and cycles
- **Notes** — An Apple Notes-inspired notes app in its own resizable window: list and editor panes, pinned notes, search, and autosave
- **A colour per feature** — the window shifts with what you're doing: neutral for Caffeine, red for Pomodoro, warm coffee for Notes
- **Dark & Light Themes** — Follows your Windows system theme or set manually
- **System Tray** — Lives in your tray with left-click toggle and right-click menu
- **Minimal & Fast** — Single-file executable, no installer needed, starts in under a second

## Installation

### Download

Grab the latest `.exe` from [Releases](../../releases) — it's a single self-contained file with no dependencies.

### Build from Source

**Prerequisites:**
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10/11

```bash
git clone https://github.com/YourUsername/caffeine-win.git
cd caffeine-win
dotnet build
```

To create a release build:

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

The output is a single `.exe` in `bin/Release/net8.0-windows/win-x64/publish/`.

## Usage

1. Run `caffeine-win.exe` — it appears in your system tray
2. **Left-click** the tray icon to toggle keep-awake on/off
3. **Double-click** to open the main window
4. **Right-click** for the context menu (Open, Pomodoro, Notes, Exit)

### Keyboard Shortcuts

**Main window**

| Key | Action |
|-----|--------|
| Escape | Close window (app stays in tray) |

**Notes window**

| Key | Action |
|-----|--------|
| Ctrl+N | New note |
| Ctrl+F | Focus the search box |
| Delete | Delete the selected note (when the list has focus) |
| Escape | Clear the search, or close the window |

### Stay Green Mode

Enable in Settings or on the Caffeine homepage. Instead of calling the Windows power API, this mode moves the mouse cursor by 1 pixel back and forth every second — keeping collaboration tools (Teams, Slack) showing you as active.

### Pomodoro Timer

Configurable work duration (15/25/45m or custom), short break (5/10m), long break (15/20/30m), and number of cycles before a long break. Optionally keeps the screen awake during work sessions.

### Notes

Click **Notes** in the title-bar tab strip (or pick Notes from the tray menu). The window expands and transforms into the notes app — the list on the left, the editor on the right — the same way switching to Pomodoro works. Switching back to Caffeine or Pomodoro shrinks it again.

Prefer it as its own window? Click the **pop-out button** in the title bar and Notes moves into a separate, resizable window, keeping whatever note you were reading. The **dock button** there puts it back.

- Each note has its own **title** field above the body; the body's first lines show as a preview in the list
- **Formatting**: click **Aa** to fold out the formatting bar — bold, italic, underline and strikethrough, a heading style, and bulleted or numbered lists (Tab nests a list item; Ctrl+B/I/U work as usual)
- **Pictures**: paste a screenshot, drag an image file in, or use the picture button — it's stored inside the note
- **Click a picture** to open it large: fit, zoom with the buttons, the scroll wheel or `+`/`−`, drag to pan, double-click to flip between fitted and actual size, Escape to close
- Notes sort by most recently edited; **pinned** notes group above the rest
- **Search** filters as you type; **right-click** a note to pin, duplicate or delete it
- Everything **autosaves** — there is no save button. Blank notes are discarded automatically
- Deleting a note moves it to **Recently Deleted** for 30 days — open it from the footer of the list to restore a note or remove it for good
- The window remembers its size, position, divider width, and which note you were reading

Notes are stored as plain JSON at `%AppData%\Caffeine\notes.json`, so they are easy to back up or inspect. Nothing is ever sent anywhere.

## Tech Stack

- .NET 8 (Windows)
- WPF (UI framework), `WindowChrome` for the resizable notes window
- Windows Forms (system tray `NotifyIcon`)
- P/Invoke (`SetThreadExecutionState`, `SendInput`)
- `System.Text.Json` for notes storage (in-box)
- No external NuGet dependencies

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and guidelines.

## License

[MIT](LICENSE) — use it however you want.
