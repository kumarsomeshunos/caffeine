# Changelog

All notable changes to Caffeine are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

**Prompt sets** — a new kind of note, for keeping a queue of prompts per application

- A `P` button beside `+` in the notes toolbar, and `Ctrl+Shift+P`, create one. Its title field asks
  for an application name rather than a note title
- Two numbered sections, **Prompts to send** and **Sent**, with a tick that moves a prompt between
  them and stamps it with the time it went
- Copy puts a prompt on the clipboard and flashes to a tick; it deliberately does not mark it sent
- Drag by the grip to reorder the queue; delete offers Undo for six seconds
- The notes list shows a `P` badge and a `3/7 sent` count on the row, and screen readers announce both
- Storage alongside the existing note bodies: `%AppData%\Caffeine\prompts\<id>.json`, written
  atomically, quarantined rather than overwritten if it fails to parse, and never written over if it
  failed to open
- Prompt sets are notes throughout — pin, search (prompt text is indexed), duplicate, and the 30-day
  Recently Deleted bin all work on them unchanged

## [2.0.0] — 2026-07-30

Caffeine grew from one tool into four. The tray window is now a place you keep things — notes and
tasks — as well as a switch that stops the screen sleeping. Nothing was taken away, and everything
still runs from one self-contained executable with no dependencies and no network access.

### Added

**Notes**
- A rich-text notepad: bold, italic, underline, strikethrough, a heading style, and bulleted or
  numbered lists
- Pictures pasted, dragged or attached, embedded in the note, with a full-size viewer that zooms,
  pans and fits
- An authored title field separate from the body, pinned notes, live search and autosave
- **Recently Deleted** — deleting is reversible for 30 days, and only permanent deletion confirms
- Storage in `%AppData%\Caffeine`: `notes.json` as an index, each formatted body its own file under
  `bodies\`, written atomically and quarantined rather than overwritten if it fails to parse
- An optional standalone window, with its size, position, divider and selection remembered

**Todo**
- Multiple task lists, each with a colour of its own, renamed and reordered from the sidebar
- Tasks with details, a due date and optional time, one level of subtasks, and daily, weekly,
  monthly or yearly repeats — completing a repeating task rolls it forward rather than finishing it
- Tray reminders when a task falls due, whether or not the window has ever been opened
- Drag to reorder, sort by date or title, a collapsible Completed section with Clear all, and
  right-click Move to / Duplicate / Add subtask
- Deleting a task is undoable for six seconds; only deleting a whole list asks first
- Storage in `%AppData%\Caffeine\tasks.json`, with the same atomic write and quarantine rules
- Row density, sort order, default due time and Completed default in Settings

**Elsewhere**
- A colour per feature: the whole window eases to red for Pomodoro, warm coffee for Notes and green
  for Todo
- `ARCHITECTURE.md`, the living description of how the app is built, and `CLAUDE.md`, the working
  rules for the codebase

### Changed

- The window is now a set of four tabs — Caffeine, Pomodoro, Notes, Todo — that transform it in
  place rather than opening separate apps. Notes and Todo can still be popped out into their own
  resizable windows
- The tab strip switched from text to icons, revealing the label only on the selected tab, and moves
  into the title bar on the wider panels
- The app mark was redrawn: a handle-less stroked coffee cup, used for the tray icon, the taskbar
  icon, the Caffeine tab and the activate button. Two wisps of steam rise and fade from it while
  caffeine is on
- Settings gained a Todo section, and its text inputs were restyled — they had been drawing a white
  Windows field on a dark card

### Fixed

- `ScrollingTextBlock` announced every character twice to assistive technology: the outgoing slot was
  hidden by opacity but still held its text, so "Active" was read as `AAccttiivvee`
- The tab indicator sized itself to the icon alone instead of the icon and its label
- The contributing guide documented a `dotnet run --project` path that never resolved

## [1.1.0] — 2026-05-21

### Added
- A sound at each Pomodoro phase change
- Smooth animated scrolling in the settings panel
- The main window now shows on first launch instead of starting tray-only

### Changed
- Single-file compression enabled, for a smaller release build
- Default window height reduced to 500px

### Fixed
- Navigating back from Settings by clicking the already-selected tab

## [1.0.0] — 2026-05-21

### Added
- Keep-awake through `SetThreadExecutionState`, with a system tray icon that reports its state
- **Stay Green** mode — a one-pixel cursor nudge each second, for collaboration-tool presence
- Auto-off timers at 15m, 30m, 1h and 2h
- A Pomodoro timer with configurable work and break durations and cycle count
- Dark and light themes, following the Windows system theme or set by hand
- `ScrollingTextBlock`, the odometer-style per-character text animation
- Start with Windows, MIT license, contributing guide, CI workflow and issue templates

[2.0.0]: https://github.com/kumarsomeshunos/caffeine/releases/tag/v2.0.0
[1.1.0]: https://github.com/kumarsomeshunos/caffeine/releases/tag/v1.1.0
[1.0.0]: https://github.com/kumarsomeshunos/caffeine/releases/tag/v1.0.0
