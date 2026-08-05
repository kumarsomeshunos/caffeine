# Caffeine

**A Windows tray app that keeps your screen awake — and keeps your focus, notes and tasks with it.**

[![Build](https://github.com/kumarsomeshunos/caffeine/actions/workflows/build.yml/badge.svg)](https://github.com/kumarsomeshunos/caffeine/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/kumarsomeshunos/caffeine?sort=semver)](https://github.com/kumarsomeshunos/caffeine/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Downloads](https://img.shields.io/github/downloads/kumarsomeshunos/caffeine/total)](https://github.com/kumarsomeshunos/caffeine/releases)

Caffeine started as one button that stops Windows going to sleep. It still is — but the same window
now also holds a Pomodoro timer, a rich-text notepad and a task list, because those are the things
you reach for during the long job you were staying awake for anyway.

One self-contained executable. No installer, no account, no NuGet packages, and **no network access
of any kind** — nothing you write ever leaves your machine.

<!-- Screenshots: drop images in docs/ and link them here. -->

## Features

**Keep awake**
- Stops Windows sleeping or blanking the display through the native `SetThreadExecutionState` API
- **Stay Green** as an alternative: nudges the cursor a single pixel each second, so Teams and Slack
  keep showing you as available
- **Auto-off** after 15m, 30m, 1h or 2h, counted from an absolute deadline so changing it mid-session
  behaves
- The tray icon *is* the state: a grey cup when idle, a blue cup with rising steam when it is working

**Pomodoro**
- Configurable work (15/25/45m or custom), short break (5/10m), long break (15/20/30m) and cycle count
- Optionally holds the screen awake through work sessions — and releases only the session it started
- A tray balloon and a short beep at each phase change

**Notes**
- Rich text: bold, italic, underline, strikethrough, a heading style, bulleted and numbered lists
- Pictures pasted, dragged or attached, stored inside the note, with a full-size viewer that zooms
  and pans
- Pinned notes, live search, autosave, and a **Recently Deleted** bin that keeps a note for 30 days

**Prompt sets** — a note that holds a queue instead of prose
- The `P` button beside `+` makes one: name the application it is for, then stack up the prompts
  you mean to send it
- **Prompts to send** and **Sent**, both numbered, with a tick to move a prompt between them and a
  copy button that puts it on the clipboard
- Drag by the grip to reorder, delete with a six-second undo, and see how far through you are
  (`3/7 sent`) from the notes list itself
- Everything else a note can do still applies — pin it, search inside it, duplicate it, bin it

**Todo**
- Multiple lists, each with its own colour
- Due dates and times, one level of subtasks, and daily/weekly/monthly/yearly repeats
- Tray reminders when a task falls due, whether or not the window is open
- Drag to reorder, sort by date or title, undo a delete, and clear completed in one go

**Throughout**
- A colour per feature — the whole window shifts: neutral for Caffeine, red for Pomodoro, warm coffee
  for Notes, green for Todo
- Dark and light themes, following the Windows system theme or set by hand
- Custom chrome, one shared motion language, and text that rolls character by character rather than
  swapping wholesale

## Install

### Download

Grab `caffeine-win.exe` from the [latest release](https://github.com/kumarsomeshunos/caffeine/releases/latest).
It is a single self-contained file — put it anywhere and run it.

Windows SmartScreen will warn you the first time, because the binary is not code-signed. *More info →
Run anyway*, or [build it yourself](#build-from-source) if you would rather not take that on trust.

**Requirements:** Windows 10 or 11, 64-bit. No .NET installation needed — the runtime is inside the exe.

### Build from source

```bash
git clone https://github.com/kumarsomeshunos/caffeine.git
cd caffeine
dotnet build          # debug build
dotnet run            # the csproj is at the repository root
```

Release build:

```bash
dotnet publish -c Release -r win-x64 --self-contained
# → bin/Release/net8.0-windows/win-x64/publish/caffeine-win.exe
```

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). A newer SDK builds it
fine; the target framework stays `net8.0-windows`.

## Usage

1. Run `caffeine-win.exe` — it appears in your system tray
2. **Left-click** the tray icon to toggle keep-awake
3. **Double-click** to open the window
4. **Right-click** for the menu: Open, Pomodoro, Notes, Todo, Exit

Closing the window does not exit the app — it keeps running in the tray. Use **Exit** from the tray
menu to quit properly.

The four tabs across the top switch what the window is. Notes and Todo widen it in place; the
**pop-out button** in the title bar moves either into its own resizable window, and the dock button
puts it back.

### Keyboard shortcuts

| Where | Key | Action |
|-------|-----|--------|
| Anywhere | `Escape` | Close the window (the app stays in the tray) |
| Notes | `Ctrl+N` | New note |
| Notes | `Ctrl+Shift+P` | New prompt set |
| Notes | `Ctrl+F` | Focus the search box |
| Notes | `Ctrl+B` / `Ctrl+I` / `Ctrl+U` | Bold, italic, underline |
| Notes | `Delete` | Delete the selected note (when the list has focus) |
| Notes | `Escape` | Clear the search, or close the picture viewer |
| Todo | `Ctrl+N` | Jump to the "Add a task" box |
| Todo | `Enter` | Add the task, or commit a rename |
| Todo | `Escape` | Close the date picker, collapse the open task, or clear the add box |

### Stay Green

Enable it in Settings or on the Caffeine panel. Instead of asking Windows to stay awake, this moves
the cursor one pixel back and forth every second, which is enough to keep collaboration tools showing
you as active. The two methods are mutually exclusive — switching while active hands over cleanly.

### Where your data lives

| What | Where |
|------|-------|
| Notes index, and each note's formatted body | `%AppData%\Caffeine\notes.json` and `bodies\` |
| Each prompt set's queue | `%AppData%\Caffeine\prompts\` |
| Tasks and lists | `%AppData%\Caffeine\tasks.json` |
| Settings, window geometry, autostart | `HKEY_CURRENT_USER\Software\CaffeineWin` |

Both files are plain JSON — back them up, inspect them, edit them. Writes go through a temp file and
a rename, so a crash mid-save cannot tear the file, and a file that fails to parse is quarantined
rather than overwritten.

Nothing is written outside your own user profile, nothing asks for elevation, and there is no
telemetry, analytics, crash reporting or update check. See [SECURITY.md](SECURITY.md).

## How it is built

- **.NET 8**, WPF for the UI, Windows Forms for the tray `NotifyIcon`
- **Zero NuGet packages** — deliberately. `System.Text.Json`, GDI+ and two P/Invokes
  (`SetThreadExecutionState`, `SendInput`) are all it uses
- No MVVM framework and no DI container; the codebase is small enough that neither pays for itself
- Every visual is code — there is not a single `.ico`, `.png` or audio file in the repository

[ARCHITECTURE.md](ARCHITECTURE.md) is the long version: component diagram, state models, the design
decisions and their rejected alternatives, persistence tables, the manual regression checklist, and
an honest list of the technical debt.

## Contributing

Issues and pull requests are welcome. [CONTRIBUTING.md](CONTRIBUTING.md) covers the setup, the code
style, and what to check before opening a PR — the short version is that the build must stay at zero
warnings, and since there is no test suite, changes are verified by hand against the checklist in
ARCHITECTURE.md.

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## License

[MIT](LICENSE) — do what you like with it.
