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
- **Todo** — A task list with everything Google Tasks does: multiple lists, due dates and times, subtasks, repeats, and reminders
- **A colour per feature** — the window shifts with what you're doing: neutral for Caffeine, red for Pomodoro, warm coffee for Notes, green for Todo
- **Dark & Light Themes** — Follows your Windows system theme or set manually
- **A cup that steams when it is working** — the tray icon, the taskbar icon and the big button all share one mark, and vapour rises from it while caffeine is on
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
4. **Right-click** for the context menu (Open, Pomodoro, Notes, Todo, Exit)

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

**Todo**

| Key | Action |
|-----|--------|
| Ctrl+N | Jump to the "Add a task" box |
| Enter | Add the task, or commit a rename |
| Escape | Close the date picker, collapse the open task, or clear the add box |

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

### Todo

Click **Todo** in the tab strip (or pick it from the tray menu). The window expands into the task list, exactly like Notes — lists on the left, tasks on the right — and the same **pop-out button** moves it into its own resizable window.

- **Add a task** from the row at the top and press Enter. Click a task's **circle** to complete it, or its **name** to open its details in place
- **Details** hold free-text notes, a due date and optional time, and up to one level of **subtasks**
- **Repeat** a dated task daily, weekly, monthly or yearly — completing it rolls it forward instead of finishing it
- **Reminders** arrive as a tray notification when a task falls due, whether or not the window is open. A task with a date but no time is due at 09:00 by default (change it in Settings)
- **Lists** live in the sidebar: add, rename, recolour, reorder, delete. Right-click a task to **move it to another list**, duplicate it, or add a subtask
- **Sort** by your own order (drag to rearrange), by date, or by title
- **Completed** tasks collect in a section at the bottom that you can collapse, or clear in one go
- **Deleting is undoable** — a task disappears straight away and an Undo button waits a few seconds. Only deleting a whole list asks you to confirm
- Settings has a **TODO** section for row density, sort order, the default due time, and whether Completed starts open

Tasks live in `%AppData%\Caffeine\tasks.json`. Like notes, they never leave your machine.

## Tech Stack

- .NET 8 (Windows)
- WPF (UI framework), `WindowChrome` for the resizable Notes and Todo windows
- Windows Forms (system tray `NotifyIcon`)
- P/Invoke (`SetThreadExecutionState`, `SendInput`)
- `System.Text.Json` for notes storage (in-box)
- No external NuGet dependencies

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and guidelines.

## License

[MIT](LICENSE) — use it however you want.
