# Architecture

> **This file is maintained by the AI. Do not edit manually unless correcting an error.** It is the single source of truth for how Caffeine is put together — read it at the start of every session, and update it before ending any session that changes files, decisions, state, persistence, debt, or known bugs.

---

## Project Description

A lightweight Windows system-tray application that keeps the screen — and optionally your "Available" presence in collaboration apps — awake, with a built-in Pomodoro timer, an Apple Notes-style notes app (including prompt sets: per-application queues of prompts to copy and tick off), and a task list. No installer, no dependencies, no background service: one self-contained `.exe` that lives in the tray.

---

## Project

- **Name:** Caffeine (project/assembly `caffeine-win`, root namespace `CaffeineWin`)
- **Type:** Windows desktop application (system-tray resident, WPF)
- **Purpose:** Prevent Windows from sleeping or blanking the display on demand, structure focus time with a Pomodoro timer, and keep notes and tasks without leaving the tray.
- **Target Users:** Windows 10/11 users who present, run long jobs, or sit in remote sessions and don't want to wiggle the mouse — plus anyone who wants a Pomodoro timer, a scratchpad and a task list that don't live in a browser tab.
- **Stage:** Released — v2.0.0, published as a single-file `.exe` on GitHub Releases.

---

## Tech Stack

| Layer | Choice | Version | Notes |
| ----- | ------ | ------- | ----- |
| Language | C# | 12 (implicit from TFM) | `Nullable` enabled, file-scoped namespaces |
| Target framework | .NET | `net8.0-windows` | Windows-only by design |
| UI framework | WPF | in-box | `UseWPF=true`; custom chrome — the tray window uses `AllowsTransparency=True` and is fixed-size, the notes window uses `WindowChrome` so it can resize and snap |
| Tray integration | Windows Forms | in-box | `UseWindowsForms=true` for `NotifyIcon` + `ContextMenuStrip`, and `Screen.AllScreens` for the notes window's on-screen check |
| Raster graphics | System.Drawing (GDI+) | in-box | Tray and window icons are drawn at runtime, not shipped as assets |
| Native interop | P/Invoke | — | `kernel32!SetThreadExecutionState`, `user32!SendInput` |
| Settings | Windows Registry (HKCU) | — | Preferences and window state only |
| Notes storage | `System.Text.Json` index + WPF `XamlPackage` bodies under `%AppData%\Caffeine\` | in-box | Both shipped in the shared framework, so still no NuGet packages. See [Configuration & Persistence](#configuration--persistence) |
| Notes editor | WPF `RichTextBox` / `FlowDocument` | in-box | Formatting and inline pictures; `EditingCommands` supplies bold/italic/underline/lists and their shortcuts |
| Build / publish | .NET SDK CLI | 8.0.x in CI, 10.0.x works locally | Self-contained, single-file, compressed, `win-x64` |
| CI | GitHub Actions | `windows-latest` | `.github/workflows/build.yml` — restore → build → publish → upload artifact |

**Toolchain note:** the project *targets* `net8.0-windows` but does not require the .NET 8 SDK to build. A newer SDK (verified with 10.0.302) restores the .NET 8 reference and runtime packs from NuGet automatically, because `SelfContained` + `RuntimeIdentifier` are set in the csproj. No .NET 8 runtime install is needed either — it is bundled into the output.

---

## Dependencies

_Zero third-party packages. This is deliberate — see [Key Design Decisions](#key-design-decisions)._

| Package | Purpose | Added |
| ------- | ------- | ----- |
| _(none)_ | Framework references only (`Microsoft.WindowsDesktop.App` via `UseWPF`/`UseWindowsForms`) | — |

`caffeine-win.csproj` contains no `<PackageReference>` elements. Adding the first one is a design decision and must be recorded here. `System.Text.Json` and `System.Windows.Shell.WindowChrome` are part of the shared framework, not packages.

---

## File Structure

```
.
├── caffeine-win.slnx              solution (single project)
├── caffeine-win.csproj            WinExe · net8.0-windows · WPF + WinForms · single-file self-contained win-x64
├── App.xaml                       Application root; ShutdownMode=OnExplicitShutdown; seeds LightTheme dictionary
├── App.xaml.cs           (474 L)  NativeMethods P/Invokes + App: tray icon, keep-awake state, 1s ticker, GDI+ icons,
│                                  the shared Notes/Todo views, 30s due-task check, create-or-raise for three windows
├── MainWindow.xaml       (882 L)  Tray window: styles/templates, 5 panels (Caffeine · Pomodoro · Notes · Todo · Settings)
├── MainWindow.xaml.cs   (1011 L)  All view logic: panel animation, gooey indicators, Pomodoro state machine,
│                                  autostart, animated window growth for the wide panels
├── NotesWindow.xaml      (134 L)  Thin shell only: rounded window, circular title-bar buttons, host for NotesView
├── NotesWindow.xaml.cs   (128 L)  Window chrome, maximise handling, geometry persistence
├── TodoWindow.xaml       (134 L)  The same shell on the Todo ambience, host for TodoView
├── TodoWindow.xaml.cs    (128 L)  Window chrome, maximise handling, geometry persistence
├── ThemeManager.cs        (86 L)  Light/dark resolution, system-theme following, registry-backed preference
├── Notes/
│   ├── Note.cs           (248 L)  The index entry: kind, title, plain-text mirror, timestamps,
│   │                              bin state, prompt counts
│   ├── Prompt.cs          (33 L)  One prompt in a prompt note: text, sent flag, sent stamp;
│   │                              pure FormatSentAt
│   └── NotesStore.cs     (261 L)  notes.json index + per-note body files (bodies\ and prompts\),
│                                  atomic writes, migration, purge-on-load, corrupt-file quarantine
├── Todo/
│   ├── TaskList.cs        (52 L)  A list: name, order, colour from an 8-swatch palette
│   ├── TodoTask.cs       (185 L)  A task: title, notes, due/time, repeat, completion, parent for subtasks;
│   │                              pure FormatDue / NextOccurrence helpers
│   ├── TodoStore.cs      (175 L)  tasks.json (lists + tasks in one snapshot), atomic writes, quarantine,
│   │                              first-run seed, sort/filter/reminder queries
│   └── TodoSettings.cs    (60 L)  Registry-backed todo preferences, read live so Settings and the view agree
├── Controls/
│   ├── NotesView.xaml   (1048 L)  The whole notes UI: list card, title/format bar/rich editor,
│   │                              prompt surface, row template, button styles, slim scrollbar,
│   │                              confirm overlay, undo toast
│   ├── NotesView.xaml.cs(1869 L)  Notes logic: selection, debounced autosave, rich-text formatting,
│   │                              pictures, prompt sets, search, pin, bin, motion, persistence
│   ├── TodoView.xaml     (683 L)  The whole todo UI: lists card, tasks card, retemplated Calendar and
│   │                              context menus, chips, confirm overlay, undo toast
│   ├── TodoView.xaml.cs (1088 L)  Todo logic: lists, rows built in code, inline detail, due picker,
│   │                              subtasks, repeat, drag reorder, delete-with-undo, persistence
│   ├── SmoothScroller.cs  (54 L)  Per-viewer wheel easing, shared by both views
│   ├── ScrollingTextBlock.xaml       host grid for the character slots
│   └── ScrollingTextBlock.xaml.cs (254 L)  odometer-style per-character slide animation (UserControl + DPs)
├── Themes/
│   ├── LightTheme.xaml    (42 L)  34 keys: 27 SolidColorBrush + 7 raw Color
│   └── DarkTheme.xaml     (42 L)  same 34 keys, dark values
├── .github/
│   ├── workflows/build.yml        CI: build + publish + artifact
│   ├── ISSUE_TEMPLATE/            bug_report.md, feature_request.md
│   └── PULL_REQUEST_TEMPLATE.md
├── README.md · CONTRIBUTING.md · LICENSE (MIT)
├── ARCHITECTURE.md                this file
└── CLAUDE.md                      assistant behavioural rules
```

There are no asset files (no `.ico`, no audio, no images) — every visual is code.

---

## Architecture Overview

One UI thread, no view-model layer, no messaging layer. `App` owns all *keep-awake* state and the tray, and is the create-or-raise owner of all three windows **and of the single `NotesView` and `TodoView` instances**. `MainWindow` is a thin imperative view over that state plus the self-contained Pomodoro machine, and hosts Notes and Todo as panels four and five. `NotesWindow` and `TodoWindow` are optional second homes for those very same views, which are reparented between the two hosts. Notes and Todo are independent of keep-awake entirely. Communication is direct method calls in every direction.

```
        ┌──────────────────────────────────────────────────────────────────────┐
        │ App : Application                                    (App.xaml.cs)   │
        │ process singleton · ShutdownMode = OnExplicitShutdown                │
        │                                                                      │
        │ state  _isActive · _activatedAt · _timerMinutes · _stayGreenMode      │
        │ owns   NotifyIcon (tray + context menu) · _ticker (DispatcherTimer 1s)│
        │                                                                      │
        │  keep-awake strategy — mutually exclusive, chosen by _stayGreenMode:  │
        │   Standard  → SetThreadExecutionState(ES_CONTINUOUS|ES_DISPLAY_REQ)  │
        │               set once on activate, cleared on deactivate            │
        │   StayGreen → SendInput(MOUSEEVENTF_MOVE, ±1px) once per tick,       │
        │               direction alternating so the cursor never drifts       │
        └───────┬───────────────────────────────────────────────▲──────────────┘
                │                                               │
   push:        │ ShowMainWindow(tab) · UpdateState()           │ pull: IsActive
   _mainWindow? │ UpdateElapsed() · ShowBalloon()               │ ActivatedAt
                ▼                                               │ TimerMinutes
        ┌───────────────────────────────────────────────────────┴──────────────┐
        │ MainWindow : Window                          (MainWindow.xaml[.cs])  │
        │ the only view · reached via CaffeineApp => (App)Application.Current  │
        │                                                                      │
        │ panels  CaffeinePanel · PomodoroPanel · NotesPanel · SettingsPanel   │
        │         exactly one Visible; swapped by AnimateToPanel(target)       │
        │ owns    _pomTimer (DispatcherTimer 1s) + Pomodoro state machine      │
        │ calls   SetActive() · SetTimer() · ShowBalloon() · StayGreenMode set │
        │ Notes is a tab like Pomodoro: selecting it eases the window from     │
        │ 380×500 to 900×620 and cross-fades NotesPanel in                     │
        └──────────────────────────────────────────────────────────────────────┘
                       ▲                                    ▲
        NotesPanel     │  App.AttachNotesTo(host) reparents │   NotesHost
        hosts it here  │  the one view between the two      │   hosts it there
                       │                                    │
        ┌──────────────┴────────────────────┐  ┌────────────┴─────────────────┐
        │ NotesView : UserControl (shared)  │  │ NotesWindow : Window         │
        │                                   │  │ optional pop-out shell       │
        │ ┌──────────────┐ ┌──────────────┐ │  │ 932×652 resizable, rounded   │
        │ │ list (280px) │ │ editor       │ │  │ title bar + dock/min/max/×   │
        │ │ ListBox +    │ │ plain TextBox│ │  │ owns geometry persistence    │
        │ │ ItemsSource  │◄►│ 1st line =  │ │  └──────────────────────────────┘
        │ │ CVS: sort    │ │ the title    │ │
        │ │  Pinned,     │ │ 600ms debounce│ │  Exactly one view exists, so
        │ │  Edited      │ │ odometer date │ │  only one store is ever open
        │ │ group        │ └──────────────┘ │  over notes.json. While popped
        │ │  PINNED/NOTES│  GridSplitter    │  out, the tray window's Notes
        │ │ filter=search│                  │  tab is disabled.
        │ └──────┬───────┘                  │
        └────────┼──────────────────────────┘
                 │ NotesStore
                 ▼
        ┌────────────────────────────┐        ┌────────────────────────────────┐
        │ %AppData%\Caffeine\        │        │ Registry — HKCU only           │
        │   notes.json               │        │ Software\CaffeineWin           │
        │ atomic write via .tmp      │        │ ...\CurrentVersion\Run         │
        │ corrupt file quarantined   │        │ ...\Themes\Personalize (read)  │
        └────────────────────────────┘        └────────────────────────────────┘
                                                      ▲        │
        ┌────────────────────────────┐                │        │
        │ ThemeManager (static)      │────────────────┘        │
        │ swaps MergedDictionaries[0]│                         │
        │ listens: SystemEvents      │◄────────────────────────┘
        │   .UserPreferenceChanged   │   both windows pick the new dictionary
        │ raises: ThemeChanged       │   up through DynamicResource
        └────────────────────────────┘
```

### Lifetime and shutdown

`ShutdownMode="OnExplicitShutdown"` is the keystone. `OnStartup` builds the tray icon, wires the ticker, then shows the window. The window's close button and `Escape` call `Hide()`, never `Close()` — so the process survives with only the tray icon visible. The **only** exit path is tray → Exit → `App.Quit()`, which flushes any in-flight note edit, unhooks `SystemEvents`, clears the execution-state request, hides and disposes the `NotifyIcon`, then calls `Shutdown()`. `App.ShowMainWindow` re-creates the window if it was genuinely closed (`_mainWindow == null || !IsLoaded`), otherwise re-shows and activates the existing instance.

`NotesWindow` really does close, unlike the tray window: it holds no state of its own, so there is nothing worth keeping in memory once `notes.json` is written. Because `Quit` never closes windows, it flushes the shared `NotesView` directly — otherwise a note typed in the last 600 ms before exiting would be lost.

### The one-second tick

A single `DispatcherTimer` on the UI thread drives everything time-based in `App`, and it only runs while active. Per tick, in order: (1) if an auto-off window is set and `Now` has reached `_autoOffAt`, deactivate and return; (2) if Stay Green is on, jiggle the mouse; (3) refresh the window's elapsed/remaining label. Auto-off is stored as an **absolute deadline**, not as an elapsed-time comparison against `_activatedAt` — the two are different clocks, because a timer can be chosen mid-session while "Active for" must still measure from activation. Pomodoro deliberately does **not** share this ticker — it has its own, started and stopped by the Start/Pause/Reset/Skip buttons.

### Panel transitions

`AnimateToPanel(target)` is the single choke point for every navigation: it fades and scales the outgoing panel to 0/0.97, collapses it on completion, makes the incoming panel visible, fades it in from 0.95 scale, and in parallel animates the window background colour and a shrink-swap-grow of the title text. `_isAnimating` guards re-entry; every navigation entry point (`Tab_Changed`, `Tab_Clicked`, `SettingsButton_Click`, `Escape`) returns early while it is set. Settings is not a tab — it is a third panel overlaid on whichever tab is checked, so leaving Settings navigates back to the checked tab, and `_previousTab` records where the user came from.

### The "gooey" selection indicators

Segmented tabs and every pill group share one animation primitive, `AnimateGooey`. Instead of sliding a fixed-width indicator, it computes the union of the current and target rects, stretches the indicator across that union by 40% of the 400 ms duration, then settles it onto the target — producing a liquid stretch-and-snap. Positions come from `TranslatePoint` against the container, so they are measured, not hard-coded; consequently every position call is guarded by `IsLoaded` and the initial layout pass is deferred to `DispatcherPriority.Loaded`.

### Theming

`ThemeManager` resolves `AppTheme.System` against `HKCU\...\Themes\Personalize\AppsUseLightTheme`, then replaces `Application.Current.Resources.MergedDictionaries[0]` wholesale. Everything referenced with `DynamicResource` in XAML re-resolves for free. The catch: colours that are *animated* in code (window background, title foreground, toggle fill/stroke) cannot use brushes from the dictionary — WPF freezes them — so each theme file exposes six raw `Color` keys alongside the brushes, and `RefreshThemeColors` must first cancel any running animation with `BeginAnimation(prop, null)` before assigning, or the animation's held value wins.

### Pomodoro state machine

### Notes

The notes feature is deliberately the least entangled part of the app: it shares only the theme dictionaries and the window icon. `NotesView` owns a `NotesStore` (the JSON file) and drives a `ListBox` through a `CollectionViewSource` — **the one place in the codebase that uses real data binding**, because a templated, grouped, filtered, sortable list is exactly what that machinery is for and hand-rolling rows would be worse code.

### Title, body, formatting and pictures

The title is **authored, not scraped** — its own `TextBox` above a divider, with the `RichTextBox` below. That split is why `Note.Title` is a stored field rather than the first line of the body, and why the row preview now starts at the body's first line instead of its second.

The editor's header is a single row: the "edited on" line centred, with an **`Aa` toggle** tucked to its right so it costs no vertical space of its own. That toggle folds the formatting bar out **above the title**, where it belongs — putting it between the title and the body split the two things that read as one. Collapsed by default, its state persisted in `NotesFormatBar`, and folded away automatically in the bin where there is nothing to format. `FormatBarHost` clips so the buttons slide out of sight rather than overhanging, and it animates to a fixed `FormatBarHeight` so the fold needs no measure pass.

The body is a `FlowDocument`. Formatting comes from WPF's own `EditingCommands` (bold, italic, underline, bullets, numbering — with Ctrl+B/I/U for free); strikethrough and the heading style are applied by hand because there are no commands for them. Two details are easy to get wrong:

- **Underline and strikethrough share one property.** Toggling either has to rebuild the `TextDecorationCollection` rather than overwrite it, or one silently clears the other.
- **Restyling text does not raise `TextChanged`.** Neither does inserting a picture. Anything that edits the document without typing has to call `MarkBodyDirty` itself — this was caught by a round-trip test showing the body file byte-identical after applying bold.

**Clicking a picture opens it in a viewer** that fills the notes surface: fit, zoom (buttons, wheel, `+`/`−`/`0`/`1`), pan by dragging, double-click to flip between fitted and actual size, and Escape, the ✕ or a click on the surround to dismiss. Two things about it are worth knowing:

- **The click cannot be handled on the `Image`.** An editable `RichTextBox` swallows mouse input to elements embedded in its document, so the click is caught on the editor and resolved with `PictureUnder` — and *that* has to go through a `TextPointer`, because `InputHitTest` on a RichTextBox answers with text elements (a `Paragraph` is not even a `Visual`, so passing one to `VisualTreeHelper.GetParent` throws) and a visual hit-test does not reach the embedded Image either. The pointer is then checked against the picture's real bounds so clicking the caret position beside a picture does not count.
- **It is an overlay, not a window.** A separate full-screen window was built first and abandoned: with `WindowStyle="None"` its content laid out larger than the window, putting the controls off-screen, and UI Automation would not enumerate it at all. The overlay reuses the pattern the delete confirmation already proves. The trade-off is honest — it covers the notes surface, not the host's title bar, so "full screen" means maximising the popped-out notes window.

Pictures arrive three ways — the toolbar's picture button, a paste, or a drag-and-drop — and all three funnel into `InsertImage`. Paste is intercepted through `DataObject.AddPastingHandler` so the context menu is covered too, and only when the clipboard holds an image *and no text*: copying from a document usually carries both, and there the text is what was meant. Every image is re-encoded to PNG through `Stabilise`/`FromStream` before it goes in, so the bitmap owns its bytes — a clipboard or file-backed source can otherwise carry a reference the document serialiser won't embed.

**Notes lives in two places but exists only once.** Selecting the Notes tab transforms the tray window exactly as Pomodoro does — the same `AnimateToPanel` cross-fade — and additionally eases the window from 380×500 out to 900×620. From there the title bar's pop-out button moves the *same* `NotesView` into `NotesWindow`, and that window's dock button moves it back. `App` owns the instance and `AttachNotesTo` reparents it, which means:

- there is never a second `NotesStore` open over `notes.json`, so the two hosts cannot fight over the file;
- popping out or docking preserves the selected note, scroll position and unsaved keystrokes for free, because nothing is rebuilt;
- while popped out the tray window's Notes tab is **disabled** — selecting it would otherwise pull the view out of the open window and leave it empty. `App.OnNotesWindowClosed` re-enables it however that window goes away (dock, ✕, or Escape), so the tab can never get stuck.

The window growth animates `Width`, `Height`, `Left` and `Top` together about the window's own centre, clamped to the working area only when the window is demonstrably on the primary monitor. Each animation hands its value back on completion (`BeginAnimation(prop, null)` then `SetValue`) — a held animation on `Left`/`Top` would silently override `DragMove` and leave the window undraggable after its first resize.

The view is configured once: sort by `Pinned` descending then `ModifiedAt` descending, filter from the search box, and group by `GroupKey` — where the group descriptions are added and removed dynamically so `PINNED`/`NOTES` headers only appear once something is actually pinned.

Three details carry most of the feel:

- **Live text, deferred order.** Typing writes straight to `Note.Body`, so the row's title and preview track your keystrokes through `INotifyPropertyChanged`. `ModifiedAt` is only bumped when the 600 ms debounce fires, and the view is never refreshed while typing — WPF's live sorting is off by default, which is what stops the row you are editing from leaping to the top of the list mid-sentence. Order settles on the next create, delete, pin, search, or reopen.
- **Rows are their own template.** The whole row lives in the `ListBoxItem` `ControlTemplate` rather than an `ItemTemplate`, so a single `IsSelected` trigger can recolour the title, preview, timestamp and pin glyph together for contrast against the amber fill. The cost is that a template with no `ContentPresenter` exposes nothing to UI Automation, so the style sets `AutomationProperties.Name` explicitly — without it every row announces `CaffeineWin.Notes.Note`.
- **Both panes are floating cards on a tinted field.** Selecting Notes turns the whole window a warm coffee (`NotesAmbient`), the way Pomodoro turns it red — and the list and editor float on it as a matched pair of rounded cards. The editor is a card rather than the bare window precisely *because* of the tint: body text must never sit on a coloured field, and the shared view is also used in the popped-out window. Selection is a *single* indicator that moves between rows with the same stretch-and-settle keyframes as the tab indicator — see below.
- **Empty notes evaporate.** Leaving a note whose body is whitespace removes it, so the list never accumulates `New Note` placeholders. `LeaveActiveNote` is the single choke point for this and for committing the editor, and it runs on selection change, on create, and on close. Notes already in the bin are exempt — a deleted note is a record to restore, not a draft to tidy away. There is one other path that changes the active note, `SyncActiveNoteToSelection`, reached when `RefreshView` drops the selection because a search no longer matches the open note; it commits explicitly rather than discarding, since a filter change is not a decision to throw a draft away.

### Prompt sets

A **prompt note** is a note *kind*, not a second feature. `Note.Kind` picks between a formatted document and a queue of prompts; everything else about the note — the list row, pinning, search, duplication, Recently Deleted, the 600 ms debounce — is untouched code paths doing exactly what they already did. The `P` button beside `+` in the list toolbar (and `Ctrl+Shift+P`) creates one. The shortcut is `P` and not `N` for a reason: **`Ctrl+Shift+N` is WPF's own `EditingCommands.ToggleNumbering`**, so the `RichTextBox` consumes it before the host window's `KeyDown` ever reaches `HandleKey` — the shortcut would have silently numbered a paragraph instead of making a note.

What changes inside the editor pane is only the body: the title field asks for an **application name**, the `Aa` toggle and the formatting bar disappear, and the `RichTextBox` gives way to two sections — `PROMPTS TO SEND (n)` and `SENT (n)` — of cards carrying a serial number, a tick, the prompt text, and copy and delete buttons.

Four decisions carry it:

- **The list is its own order.** Position in the JSON array is position on screen, so there is no order field to keep in step with anything. Sending a prompt is `Remove` then `Add` — a move to the very end, which puts it at the bottom of Sent and makes the section read oldest-first. Taking one back inserts it at the end of the unsent block. `NormalisePrompts` straightens out a hand-edited file on the way in and is the only place the invariant "sent always follows unsent" is enforced.
- **`PlainText` mirrors the prompt texts**, exactly as it mirrors a document's. That is what makes search, the row preview and blank-note discard work on prompt notes with no special-casing at all — the single most valuable consequence of building this as a note kind.
- **`RebuildPrompts` is the only thing that puts a prompt on screen,** and it runs on *structural* change only: add, delete, tick, reorder, load. Typing writes straight through to the model and never rebuilds, because a rebuild mid-keystroke would take the caret with it. This is `TodoView.Rebuild()`'s contract with the typing exemption the notes list already has.
- **The grip is the drag handle, not the card.** A card's middle is an editable `TextBox`, where dragging has to mean selecting text. The grip is a *transparent `Border` around* the two-bar `Path` rather than the path itself — WPF hit-tests a `Path` against its stroke, which would have made a 1.3px line the whole target.

Deleting a prompt is undoable rather than confirmed — a six-second toast, matching Todo's reasoning that a prompt is cheap to retype and expensive to be interrupted over. Unlike Todo the write is *not* deferred to the end of the undo window: the debounce can fire inside those six seconds for an unrelated edit, so deferral would only pretend to hold the deletion back. Copy is inert by design; it puts the text on the clipboard and flashes the icon to a tick, and marking sent stays a separate deliberate act.

Two hazards the prompt file handles explicitly. `LoadPrompts` returns `null` — distinct from an empty list — when the file exists but could not be *read*, **or when it failed to parse and the quarantine rename also failed**, because in both cases the original is still sitting on disk. `_promptsReadOnly` then blocks every write for that note. Crucially it also blocks the two places that mirror the queue back into the *index* — `CommitEditor` and `RebuildPrompts` — because an empty `_prompts` is what an unreadable file looks like, and mirroring that would erase `PlainText`, zero the counts, and make `LeaveActiveNote` discard the note as blank. `IsEmpty` is not trusted for a prompt note whose set could not be read.

**The index and the prompt file must be read with the same `JsonSerializerOptions` they were written with.** `Note.Kind` serialises through `JsonStringEnumConverter`, and `System.Text.Json`'s default converter reads enums as numbers only — so a `Load` that omitted the options threw `JsonException` on the very file `Save` had just written, quarantined it, and presented an empty notes list. `TodoStore.Load` already passes its options; `NotesStore` must too. This is the failure mode to remember whenever a converter is added to a store.

### Recently Deleted

Deleting is reversible, so it does not interrupt: `Delete` stamps `Note.DeletedAt` and the note drops out of the list. The bin and the live list are **two views over one collection** — `View_Filter` accepts a note only when `note.IsDeleted == _showingBin` — so nothing is copied or moved between stores.

- The footer of the list card is the way in and the way back: it reads `Recently Deleted` with a count, or `‹ All Notes` in accent while the bin is showing. It hides entirely when the bin is empty and you are not in it.
- In the bin the toolbar swaps: new-note and pin give way to **Restore**, and the trash means *permanently*. That is the only delete that asks for confirmation, because it is the only one that cannot be undone. Pinning is meaningless there, so grouping is switched off too.
- Rows trade their edit timestamp for a countdown — `30 days left`, then `1 day left`, then `Deleting today` — from `Note.DaysLeft`.
- The editor is read-only in the bin. Restore it before editing.
- **Purging happens on load**, in `NotesStore.Load`: anything past `Note.RetentionDays` is dropped and the file is rewritten so it is genuinely gone. One rule, one place, no timer.

### Todo

Todo is Notes' sibling in construction and Google Tasks' in behaviour, wrapped in the app's own surfaces. It has the same shape: a single `TodoView` owned by `App`, reparented between the tray window's `TodoPanel` and `TodoWindow`, over a single `TodoStore`. Selecting it tints the whole window `TodoAmbient` — a muted green, the colour of *done* — and floats a lists card and a tasks card on it, at exactly the geometry Notes uses.

Where Notes leans on data binding, **Todo builds its rows in code**. The sidebar is a bound `ListBox` because it is a plain list, but a task row is a small tree that changes shape — tick, title, due chip, repeat and notes glyphs, a subtask counter, and an inline detail panel that unfolds beneath it — and it also has to be draggable. Hand-building it in `BuildRow` keeps that in one readable method instead of a template plus five converters plus an `IsExpanded` field on the model.

- **One task is expanded at a time.** Clicking a row's *body* opens its detail in place; clicking its *tick* completes it. Nothing navigates, so the list never loses its place. `Rebuild()` re-renders the whole list on every change — it is a few dozen elements, and it means there is exactly one code path that puts a task on screen.
- **Completing a repeating task advances it** instead of finishing it, matching Google Tasks: the due date rolls to the next occurrence and the task stays live. Completing a parent completes its subtasks, because a half-done finished task is a lie.
- **Subtasks are ordinary tasks with a `ParentId`,** one level deep only. They travel with their parent when it moves list, and they are deleted with it.
- **Deleting is undoable, not confirmed.** A deleted task disappears immediately and a toast offers Undo for six seconds; the write to disk is deferred until that window closes, so an undo costs nothing. Deleting a *list* is the one destructive action that asks first, because it takes its tasks with it.
- **Due dates are a popup, not a dialog.** Quick chips (Today · Tomorrow · Next week), a retemplated `Calendar`, and an optional time. A task with a date but no time falls due at `TodoSettings.DefaultDueHour`.
- **Reminders come from a 30-second timer in `App`,** not the view — a task must be announced whether or not the tab has ever been opened. `TodoTask.Notified` makes it fire once per due date and resets whenever the date changes.
- **Sorting is a mode, not a rearrangement.** `My order` is the stored `Order`; Date and Title are views over it, and drag-to-reorder is disabled outside `My order` because there would be nothing to reorder.

#### Retemplating stock controls

Todo is the first feature to reach for WPF controls that ship with a Windows look — `Calendar`, `ContextMenu`, `MenuItem` — and each needed the whole template replaced, not restyled. Three traps, all found the hard way:

- **`Calendar` ignores implicit styles for its cells.** `CalendarItem` creates the day and month buttons in code and applies only what the `Calendar` itself names, so the styles have to be set through `CalendarDayButtonStyle` and `CalendarButtonStyle`. An implicit `TargetType="CalendarDayButton"` style silently loses to the theme.
- **A menu resolves its separators by key** — `MenuItem.SeparatorStyleKey`, not an implicit `TargetType="Separator"`.
- **`Data="{TemplateBinding Tag}"` does not build a `Geometry`.** Bindings skip type converters, so a path string in `Tag` yields nothing at all — the nav arrows simply never drew. Geometry has to be written into the template.

And one runtime crash worth remembering: **closing a `Popup` from inside a mouse-up handler** makes WPF release capture mid-route and re-deliver the event into the tree it is tearing down, which throws inside `CalendarItem`. `CloseDuePopup` defers the close to `DispatcherPriority.Input`.

### Shared motion language

Both windows animate from the same vocabulary, so Notes feels like the same application rather than a bolted-on window:

| | Value |
| --- | ----- |
| `BubbleEase` | `CubicEase` EaseOut — arrivals, hover, press |
| `SoftEase` | `QuadraticEase` EaseInOut — fades and colour transitions |
| Hover / press | scale 1.06 in 150 ms out 200 ms / 0.94 in 80 ms (1.12 / 0.9 for the small icon buttons) |
| Arrival | opacity 0→1 over 200–220 ms with a shallow scale from 0.97–0.99 |
| Wheel scrolling | 16 ms tick closing 20% of the remaining gap per frame |

**Colours snap, transforms animate.** This is not a shortcut — dictionary brushes arrive frozen and animation `To` values cannot resolve `DynamicResource`, so the tray window's own templates set colours with plain setters and animate only transforms. Notes follows the same rule, which is why the amber selection fill appears instantly while the row eases in from the left.

`ScrollingTextBlock` — the app's odometer text — is used for the editor's "edited on" line, so a save rolls the minute over instead of replacing the string. A note switch rolls the whole line, but that happens beneath the editor's fade-in and so reads as a single motion.

### The mark

One coffee cup, drawn as strokes on a 24×24 grid: a single continuous path for the rim, both tapering walls and the rounded base — **no handle**, which is what makes the silhouette symmetric and lets it centre cleanly inside a circle. Above it sit two wisps of steam, mirrored about the cup's centre at x = 10. It is the app's whole visual identity and it renders from the same path data everywhere — tray icon, taskbar and Alt-Tab icon, the Caffeine tab, and the big activate/deactivate button.

- `App.MarkBody` / `MarkSteamLeft` / `MarkSteamRight` hold the geometry. `DrawMark` strokes it into a `DrawingVisual`, `RenderMark` rasterises that to a `RenderTargetBitmap`.
- The tray icon is grey (`#8E8E93`) when idle and accent blue (`#0A84FF`) with steam when active; the window icon is always the blue steaming version.
- Optical centring is explicit: `MarkNudgeX` (+2) centres the cup horizontally, and the vertical nudge differs by state (+1.95 with steam, −1.2 without) because steam occupies the top of the grid. Without that the idle tray icon sits visibly low.
- **Steam is the state signal wherever the mark reports state.** On the tray icon it appears only when caffeine is on. On the toggle button it does the same, and it *moves*: each wisp drifts up through `SteamCycle` (2.6 s) while fading in low and thinning out at the top, the two half a cycle apart so they never rise in lockstep. The whole mark slides between its idle and steaming nudge as the steam arrives, which is the same re-centring the tray icon does in one step.
- **The toggle draws the mark on its own 24-unit `Canvas` inside a `Viewbox`,** rather than stretching each path separately — three independently stretched paths would each fill their own box and lose register with one another. `MarkHost`'s nudge transform and both wisps' rise transforms are created in `ResolveMark` and assigned in code, because a `Freezable` written into a `ControlTemplate` is sealed and cannot be animated.
- **The Caffeine tab always steams.** There the mark names a feature rather than reporting a state, and a bare cup read as a bucket beside the stopwatch and the page. Its glyph box is 21px tall against its siblings' 17 so the cup keeps the same weight once the steam has taken the top third.
- **The geometry is duplicated twice**, as the `CupIcon`/`SteamLeft`/`SteamRight` paths on the toggle and as the Caffeine tab glyph in `MainWindow.xaml`, because XAML cannot reference a C# string constant as path data. All sites carry a comment; change them together.

Rendering through WPF rather than GDI+ also retired the old HICON leak: `DrawTrayIcon` now clones the icon and calls `DestroyIcon` on the handle, and `SetTrayIcon` disposes the icon it replaces.

### Shared control geometry

The notes window is not styled independently; it borrows the tray window's measurements so the two read as one application. Anything new should keep to this table rather than inventing sizes.

| Element | Value | Taken from |
| ------- | ----- | ---------- |
| Window shell | 12px radius, 16px shadow gutter, `DropShadowEffect` blur 24 / opacity 0.15 | `MainWindow.xaml` root border |
| Title bar | `14,14,20,10` padding — the 14 on the left is deliberate, see the tab strip below — circular buttons on the right, **no title text** | tray title bar |
| Tab strip | `SurfaceColor` container, radius 10, padding 3; segments radius 8, padding `14,6`, 13px Medium | the tab strip itself |
| Floating cards | `SurfaceColor`, radius 14, 14px from the window edge | notes list card |
| Title-bar buttons | 28×28 circles (radius 14), `SurfaceColor` → `SurfaceHover`, glyph 11–13px `SecondaryText`, 8px gaps | tray gear / close buttons |
| Icon buttons | the same 28px circles | tray gear / close buttons |
| Section labels | 11px SemiBold `SecondaryText`, uppercase | `AUTO-OFF`, `APPEARANCE`, `GENERAL` |
| Cards and rows | 12px radius | tray cards |
| Text inputs | 14px radius on `SurfaceColor` | tray pill inputs |
| Primary buttons | pill: radius 20, padding `22,11`, 14px SemiBold | `ActionButtonStyle` |
| Secondary buttons | radius 18, padding `18,10`, 13px | `SecondaryButtonStyle` |

Deliberately **not** copied: the tray window's red Windows-style close button (it never had one — the close control is a neutral circle), and system caption buttons of any kind.

### The travelling selection

Selecting a note uses the tab strip's motion turned on its side. One `RowIndicator` border lives **inside** the `ListBox`'s retemplated `ScrollViewer`, as a sibling of the `ItemsPresenter`, so it scrolls with the rows for free. `MoveRowIndicator` measures the selected `ListBoxItem` with `TranslatePoint`, then animates `Y` and `Height` through the same two spline keyframes as `AnimateGooey`: stretch to span both the old and new rows by 40% of 400 ms, then settle onto the target. Rows therefore carry no selected fill of their own — only their text recolours.

Three things make it hold up:

- **A `Freezable` declared inside a `ControlTemplate` is sealed**, so the indicator's `TranslateTransform` cannot be declared in XAML — animating it throws `InvalidOperationException` at runtime. `ResolveIndicator` assigns a code-created transform instead. The same trap as frozen dictionary brushes, one layer deeper.
- `Template.FindName` is only valid once the template has been applied, so `ResolveIndicator` waits for `IsLoaded` and calls `ApplyTemplate` first.
- Row heights vary with how far the preview wraps, and the list scrolls, so `LayoutUpdated` snaps the indicator back into place whenever layout settles — skipped while it is mid-travel so the correction cannot fight the animation. Virtualisation is off so the container always exists to measure.

The one place Notes deliberately improves on the tray window is scrolling: `SmoothScroller` is instantiated **per `ScrollViewer`** rather than sharing one target field, so easing the list cannot disturb the editor mid-animation. That is the bug logged against `MainWindow.AnimateScroll` in Technical Debt — the fixed shape now exists to port back.

### Rounded shell

Matching the tray window's 12px corners on a *resizable* window needs both mechanisms at once: `AllowsTransparency="True"` for the rounded `Border` and its shadow gutter, plus `WindowChrome` for real dragging, snapping and edge resizing. Two details make it hold together:

- **`ClipToBounds` does not clip to a corner radius** in WPF, so the panes round their own outer corners instead — `0,0,0,12` on the list, `0,0,12,0` on the editor — and the title bar is transparent so the shell's own fill provides the top corners.
- **Maximised means edge-to-edge.** `OnStateChanged` zeroes the 16px margin and every corner radius, or the desktop would show through a transparent gutter around a maximised window. The save-error message is a floating toast rather than its own grid row for the same reason: a full-width strip at the bottom would square off the shell.

### Pomodoro state machine

Two orthogonal enums: `PomTimerState { Idle, Running, Paused }` and `PomodoroPhase { Work, ShortBreak, LongBreak }`. `PomStartPause_Click` is the transition table for the former. `PomAdvancePhase` handles the latter: Work → LongBreak when `_pomCurrentCycle >= _pomTotalCycles` (resetting the counter to 1), otherwise Work → ShortBreak; any break → Work, incrementing the cycle after a short break. Phase completion stops the timer, drops keep-awake if the finished phase was Work and "Keep screen awake" is on, shows a tray balloon, plays the beep pattern, advances the phase, and returns to `Idle` with the display reset. The progress ring is a `Path` whose `Geometry` is re-parsed each tick from trigonometry against a 90/90 centre and radius 87.

---

## State Models

There is no database and no serialised document model. State lives in two objects, and only three values outlive the process.

### `App` — keep-awake state (in memory)

| Field | Type | Meaning | Invariants |
| ----- | ---- | ------- | ---------- |
| `_isActive` | `bool` | Is a keep-awake request in force? | Exposed read-only as `IsActive`; only ever changed via `SetActive` |
| `_activatedAt` | `DateTime` | When the current session began | Stamped on every activation and never moved afterwards — it is the clock behind "Active for", so `SetTimer` must not touch it |
| `_timerMinutes` | `int` | Auto-off window in minutes; `0` = none | Forced to `0` on deactivate; `MainWindow.SyncTimerSelection` mirrors it back onto the pills so the UI cannot advertise a window that no longer exists |
| `_autoOffAt` | `DateTime` | Absolute deadline for auto-off | Only meaningful while `_timerMinutes > 0`; every read is gated on that, so no stale value is observable. Set on activation *and* whenever a timer is chosen mid-session |
| `_stayGreenMode` | `bool` | Jiggle instead of the power API | Persisted; setter re-applies the method mid-session and refreshes the view |
| `_jiggleForward` | `bool` | Direction of the next 1px nudge | Flips every jiggle so net cursor movement is zero |

### `MainWindow` — Pomodoro state (in memory, not persisted)

| Field | Type | Default | Notes |
| ----- | ---- | ------- | ----- |
| `_pomState` | `PomTimerState` | `Idle` | Drives the Start/Pause/Resume button label |
| `_pomPhase` | `PomodoroPhase` | `Work` | |
| `_pomCurrentCycle` | `int` | `1` | 1-based, resets to 1 after a long break |
| `_pomTotalCycles` | `int` | `4` | Work sessions before a long break |
| `_pomWorkMinutes` | `int` | `25` | 15 / 25 / 45 / custom |
| `_pomShortBreakMinutes` | `int` | `5` | 5 / 10 / custom |
| `_pomLongBreakMinutes` | `int` | `15` | 15 / 20 / 30 / custom |
| `_pomRemaining` / `_pomPhaseTotal` | `TimeSpan` | derived | Ratio drives the progress arc |
| `_pomHeldCaffeine` | `bool` | `false` | Did *Pomodoro* start the current keep-awake session? Gates release so a hand-started session is never cancelled. Cleared by `UpdateState` whenever the session ends by any other means |

### `Note` — the only real data model (persisted)

Serialised to `notes.json`; everything else on the type is derived and marked `[JsonIgnore]`.

| Field | Type | Notes |
| ----- | ---- | ----- |
| `Id` | `string` | `Guid` hex, assigned at construction. Used to restore the last selection |
| `Kind` | `NoteKind` | `Text` or `Prompt`. Absent from index files written before prompt sets, which read back as `Text` — which is what they were. Serialised as a string, so the index stays inspectable |
| `Title` | `string` | Authored in its own field. `DisplayTitle` substitutes "New Note" — or "New Prompt Set" — while it is blank |
| `PlainText` | `string` | Plain-text mirror of the formatted body, refreshed on save. Search and row previews read this so neither has to open a document |
| `HasImages` | `bool` | Set when the body holds pictures. Without it an image-only note looks blank to `IsEmpty` and gets discarded on the way out |
| `Body` | `string?` | Legacy only — the single plain-text body written before rich text. `NotesStore.Load` folds it into `Title`/`PlainText` and clears it |
| `CreatedAt` | `DateTime` | Never changes |
| `ModifiedAt` | `DateTime` | Bumped by the debounced commit, not per keystroke — this is what the list sorts on |
| `Pinned` | `bool` | Drives `GroupKey` and the primary sort. Cleared when a note goes to the bin |
| `DeletedAt` | `DateTime?` | Null for a live note; stamped when it moves to Recently Deleted. The single flag that decides which of the two list views a note appears in |
| `PromptTotal` · `PromptSent` | `int` | Prompt notes only. Mirrored into the index for the same reason `PlainText` is — the row must render `3/7 sent` without opening the prompt file. Settled by `RebuildPrompts`, so they move the moment a tick does rather than waiting for the debounce |

| Derived | Rule |
| ------- | ---- |
| `Title` | First non-blank line, trimmed, capped at 120 chars; `"New Note"` when blank |
| `Preview` | The next two non-blank lines joined by `\n`; `"No additional text"` when there are none |
| `IsDeleted` · `DaysLeft` | In the bin; and whole days before purging, from `RetentionDays` (30) |
| `TimeLabel` | Live: `Today HH:mm` → `Yesterday` → weekday name (< 7 days) → `dd/MM/yyyy`. In the bin: `30 days left` → `1 day left` → `Deleting today` |
| `EditedOnLabel` | `d MMMM yyyy 'at' HH:mm`, for the centred line above the editor |
| `GroupKey` | `PINNED` or `NOTES` — already in display form, so the header template needs no converter |
| `IsEmpty` | Whitespace-only body; the trigger for silent discard |
| `IsPrompt` | `Kind == Prompt`. Drives the row's `P` badge, the count, and the editor's mode |
| `PromptCountLabel` | `3/7 sent`, for the row beside the timestamp |
| `AccessibleLabel` | What the row *announces*. The row template has no `ContentPresenter`, so the badge and count never reach the automation tree — this spells them out instead |

`DeriveTitle`, `DerivePreview`, `FormatListTimestamp` and `FormatEditorTimestamp` are `static` and pure — the first genuinely unit-testable logic in the codebase.

### `Prompt` — one entry in a prompt note's queue (persisted)

Serialised as a JSON array in `prompts\<id>.json`. **The array's order is the model's order** — there is deliberately no `Order` field and no `Id`, because nothing needs one: the sections are a partition of one ordered list, and every operation is a move within it.

| Field | Type | Notes |
| ----- | ---- | ----- |
| `Text` | `string` | The prompt itself. Multi-line; the card's `TextBox` grows to fit |
| `Sent` | `bool` | Which of the two sections it sits in. Every sent prompt follows every unsent one |
| `SentAt` | `DateTime?` | When it was ticked; null while it waits. Drives the `Sent today 14:22` line and orders Sent after a `NormalisePrompts` |

| Derived | Rule |
| ------- | ---- |
| `IsEmpty` | Whitespace-only. An unsent empty prompt is pruned on the way out of the note |

`Prompt.FormatSentAt` is `static` and pure, and takes `now` as a parameter like the rest of the derivation helpers.

### `TaskList` and `TodoTask` — the todo data model (persisted)

Both serialise into one `tasks.json` snapshot; derived members are `[JsonIgnore]`.

**`TaskList`**

| Field | Type | Notes |
| ----- | ---- | ----- |
| `Id` | `string` | `Guid` hex. Tasks reference it; it is also what "reopen on last list" stores |
| `Order` | `int` | Sidebar position; rewritten as a dense sequence on every move |
| `Name` | `string` | `DisplayName` substitutes "Untitled list" while blank |
| `Colour` | `string` | Hex from `TaskList.Palette` (8 swatches). Tints the tick circles of its tasks — the only place list colour appears |

**`TodoTask`**

| Field | Type | Notes |
| ----- | ---- | ----- |
| `Id` · `ListId` | `string` | Identity and owning list |
| `ParentId` | `string?` | Set on a subtask. One level only; a task with a parent can never have children |
| `Order` | `int` | Position within its list (or under its parent) in `My order` |
| `CreatedAt` · `CompletedAt` | `DateTime` / `DateTime?` | `CompletedAt` orders the Completed section, newest first |
| `Notified` | `bool` | Set once the due balloon has been shown. Cleared whenever `Due` or `HasTime` changes, so a rescheduled task announces again |
| `Title` · `Notes` | `string` | `DisplayTitle` substitutes "New task"; `Notes` is the inline detail |
| `Due` | `DateTime?` | Date, with a meaningful time only when `HasTime` |
| `HasTime` | `bool` | Distinguishes "Friday" from "Friday 14:30" |
| `Repeat` | `Recurrence` | `None` · `Daily` · `Weekly` · `Monthly` · `Yearly` — presets only |
| `Completed` | `bool` | Moves the task into the Completed section |

| Derived | Rule |
| ------- | ---- |
| `DueAt` | `Due` when `HasTime`; otherwise `Due` at `TodoSettings.DefaultDueHour`. Both sorting and the reminder read this, so they can never disagree |
| `IsOverdue` · `IsDueToday` | Against `DateTime.Now`; `IsOverdue` is what turns the due chip red |
| `DueLabel` | `Today` → `Tomorrow` → `Yesterday` → weekday (< 7 days) → `d MMM`, plus `· HH:mm` when timed |
| `RepeatLabel` | Reads off the due date: "Every Friday", "Monthly on the 14th" |
| `IsEmpty` · `HasNotes` · `IsSubtask` · `Repeats` · `HasDue` | Straight predicates used by the row builder |

`TodoTask.FormatDue` and `TodoTask.NextOccurrence` are `static` and pure, as is everything in `TodoStore`'s query section.

### Persisted state

`StayGreenMode`, `Theme`, the autostart entry, the two feature windows' geometry, the notes splitter/selection, the todo sidebar width and last list, the todo preferences, and the notes and tasks themselves survive a restart. Everything else — all Pomodoro durations and the cycle count — resets to defaults on launch.

---

## API Surface

No network or CLI surface. "API" here means the contracts between the four components; treat these as the seams to respect when changing code.

| Component | Member | Purpose | Called by |
| --------- | ------ | ------- | --------- |
| `App` | `IsActive` · `ActivatedAt` · `TimerMinutes` | Read-only state for the view | `MainWindow.UpdateElapsed/UpdateState` |
| `App` | `AutoOffRemaining` | Time left before auto-off, or `TimeSpan.Zero` when no timer is running | `MainWindow.UpdateElapsed` — the view never does the deadline arithmetic itself |
| `App` | `StayGreenMode { get; set; }` | Toggle strategy; persists, re-applies live, refreshes view | Both Stay Green toggles |
| `App` | `ToggleActive()` · `SetActive(bool)` | The only mutators of keep-awake state | Tray left-click, power button, Pomodoro |
| `App` | `SetTimer(int minutes)` | Set auto-off window; activates if currently off | Auto-off pills |
| `App` | `ShowMainWindow(string tab = "caffeine")` | Create-or-raise the window on a given tab | Tray menu, tray double-click, startup |
| `App` | `ShowBalloon(title, message)` | 3 s tray notification | Pomodoro phase completion |
| `App` | `NotesView` | The single shared notes view, created on first use | Both hosts |
| `App` | `AttachNotesTo(Panel host)` | Reparent the view, detaching it from its previous host | `MainWindow.AnimateToPanel`, `NotesWindow` load |
| `App` | `ShowNotes()` | Open Notes wherever it currently lives | Tray menu |
| `App` | `PopOutNotes()` · `DockNotes()` | Move Notes between the tray panel and its own window | Pop-out / dock buttons |
| `App` | `NotesPoppedOut` · `OnNotesWindowClosed()` | Track and reset the popped-out state | `MainWindow`, `NotesWindow.OnClosing` |
| `MainWindow` | `SetNotesPoppedOut(bool)` | Disable the Notes tab while the view is elsewhere | `App` |
| `NotesView` | `Flush()` · `PersistState()` | Commit to disk / save divider and selection | Hosts on close, `App.Quit` |
| `NotesView` | `HandleKey(KeyEventArgs)` | Notes shortcuts, routed from the host window | Both hosts' `KeyDown` |
| `App` | `TodoStore` · `TodoView` | The single shared store and view, the view created on first use | Both hosts |
| `App` | `AttachTodoTo(Panel host)` | Reparent the todo view, detaching it from its previous host | `MainWindow.AnimateToPanel`, `TodoWindow` load |
| `App` | `ShowTodo()` | Open Todo wherever it currently lives | Tray menu |
| `App` | `PopOutTodo()` · `DockTodo()` | Move Todo between the tray panel and its own window | Pop-out / dock buttons |
| `App` | `TodoPoppedOut` · `OnTodoWindowClosed()` | Track and reset the popped-out state | `MainWindow`, `TodoWindow.OnClosing` |
| `App` | `RefreshTodoSettings()` | Re-read todo preferences and rebuild the list | Settings handlers, Reset to Defaults |
| `MainWindow` | `SetTodoPoppedOut(bool)` | Disable the Todo tab while the view is elsewhere | `App` |
| `TodoView` | `Flush()` · `PersistState()` | Commit to disk / save sidebar width and last list | Hosts on close, `App.Quit` |
| `TodoView` | `AnimateIn()` · `HandleKey(KeyEventArgs)` · `RefreshSettings()` | Arrival motion, shortcuts, settings re-read | Hosts, `App` |
| `TodoStore` | `TopLevel` · `Children` · `Sorted` · `DueForReminder` · `NextOrder` | The queries the view and the reminder check share | `TodoView`, `App.CheckDueTasks` |
| `NotesView` | `AnimateIn()` · `SetOuterCornerRadius(double)` | Arrival motion / flatten corners when maximised | Hosts |
| `App` | `Quit()` | The single clean exit path; flushes notes first | Tray → Exit |
| `App` | `static CreateWindowIcon()` | 32px GDI+ window icon as `ImageSource` | `MainWindow` ctor |
| `MainWindow` | `ShowTab(string tab)` | Check the requested tab's radio button | `App.ShowMainWindow` |
| `MainWindow` | `UpdateState()` · `UpdateElapsed()` | Push refresh from `App` | `App` on state change and per tick |
| `ThemeManager` | `Initialize()` · `Shutdown()` | Load preference, apply, hook/unhook `SystemEvents` | `App.OnStartup` / `App.Quit` |
| `ThemeManager` | `ApplyTheme(AppTheme)` · `SavePreference(AppTheme)` | Swap dictionary / persist choice (separate on purpose) | Settings radio buttons, system-theme change |
| `ThemeManager` | `IsDark` · `CurrentSetting` · `event ThemeChanged` | Resolved state + notification | `MainWindow.OnThemeChanged` |
| `NotesWindow` | `FlushPendingSave()` | Commit the editor and write to disk without closing | `App.Quit` |
| `NotesStore` | `Notes` (`ObservableCollection<Note>`) | The bound collection; mutating it updates the list | `NotesWindow` |
| `NotesStore` | `Load()` · `Save()` | JSON round-trip; `Save` is atomic and returns success | `NotesWindow` on every mutation |
| `NotesStore` | `LoadPrompts(id)` | A prompt note's queue. Empty list = no file yet; **`null` = the file is there and unreadable**, and the caller must not write over it | `NotesView.LoadPromptSet`, `Duplicate_Click` |
| `NotesStore` | `SavePrompts(id, prompts)` · `DeletePrompts(id)` | Atomic write / remove, matching the body-file pair | `NotesView`, purge-on-load |
| `NotesStore` | `static PromptFolder` · `PromptPath(id)` | `%AppData%\Caffeine\prompts\<id>.json` | — |
| `Prompt` | `static FormatSentAt(sent, now)` | The stamp under a sent prompt | `Prompt`'s card |
| `NotesStore` | `LastError` | Non-null after a failed load or save; rendered as a red strip | `NotesWindow.ReportStoreError` |
| `NotesStore` | `static FolderPath` · `static FilePath` | `%AppData%\Caffeine\notes.json` | — |
| `Note` | `static DeriveTitle` · `DerivePreview` · `FormatListTimestamp` · `FormatEditorTimestamp` | Pure derivation from the body | `Note`'s own properties |
| `ScrollingTextBlock` | `Text` · `TextFontSize` · `TextFontWeight` · `TextForeground` · `TextFontFamily` · `AnimationMilliseconds` | Dependency properties; setting `Text` animates changed characters only | 5 usages in `MainWindow.xaml` |
| `NativeMethods` | `SetThreadExecutionState(uint)` | Suppress display sleep | `SetActive`, `ReapplyKeepAwakeMethod`, `Quit` |
| `NativeMethods` | `SendInput(uint, INPUT[], int)` | Synthesise the 1px mouse move | `JiggleMouse` |

---

## Key Design Decisions

| Decision | Alternatives considered | Why this won |
| -------- | ----------------------- | ------------ |
| **State lives in `App`, not a view model; no MVVM, no bindings to a VM** | MVVM with `INotifyPropertyChanged`, a DI container | The tray — not the window — owns the process lifetime, so state must outlive the view. At this size, `_mainWindow?.UpdateState()` is less machinery than a binding graph. Accepted cost: `MainWindow.xaml.cs` is large and untestable. |
| **`ShutdownMode="OnExplicitShutdown"`** | `OnMainWindowClose` with a hidden dummy window | A tray app must survive its window. This makes hide-on-close correct by construction rather than by defensive event handling. |
| **Icons drawn at runtime from vector paths** | Ship `.ico`/`.png` assets; keep the old GDI+ drawing calls | Keeps the repo asset-free, lets the tray icon express state (grey cup vs blue cup with steam) without shipping two files, and means one definition of the mark serves every size. Rendering through WPF rather than GDI+ also removed the HICON leak the old code had on every state change. |
| **Registry (`HKCU\Software\CaffeineWin`) for *settings*** | JSON/INI next to the exe, `%AppData%` file, `Settings.settings` | For preferences and window state, HKCU is per-user, always writable, needs no path handling and has no file I/O error paths — and the single-file exe's extraction directory is not a durable home. Scoped to settings only: user *content* goes to `%AppData%` (see the notes row below). |
| **Two colour representations per theme (23 brushes + 6 raw `Color`s)** | Unfreeze/clone brushes at use sites; one dictionary of brushes only | Dictionary brushes arrive frozen and cannot be animated. Exposing the six animated colours as raw `Color` keys keeps the animation code honest about which values it mutates. Cost: the two lists must be kept in sync across both theme files. |
| **Stay Green as an alternative to, not an addition to, the power API** | Run both simultaneously | Presence in Teams/Slack tracks *input*, not power state, so the two solve different problems and running both would move the cursor for no reason. `ReapplyKeepAwakeMethod` enforces exclusivity when the toggle flips mid-session. |
| **Alternating ±1px jiggle** | Fixed +1px nudge, `SetCursorPos`, key synthesis | Alternating direction means zero net drift, so the cursor never walks across the screen during a long session. `SendInput` (not `SetCursorPos`) is what actually resets the idle timer. |
| **`Console.Beep` for the Pomodoro chime** | Ship a `.wav`, `SystemSounds`, NAudio | No asset, no dependency, audible over most output devices. Cost: it blocks a thread-pool thread for ~1.8 s (hence `Task.Run`) and cannot be muted. |
| **Zero NuGet packages** | MahApps/ModernWpf for chrome, a settings library, a JSON library, CommunityToolkit.Mvvm | Every dependency is a supply-chain and startup-cost decision for a tray app that must launch in under a second. The custom chrome and styles come to roughly 500 lines of XAML across the two windows — cheaper than a themepack — and `System.Text.Json` is already in the framework. |
| **Single-file, self-contained, compressed publish** | Framework-dependent build, MSI/MSIX installer | "Download one exe and run it" is the product promise. Users need no .NET install; there is nothing to uninstall. |
| **Pomodoro owns its own `DispatcherTimer`** | Reuse `App._ticker` | The ticker only runs while keep-awake is active; Pomodoro must run independently of it and be pausable on its own. |
| **Auto-off is an absolute deadline (`_autoOffAt`), not elapsed-vs-`_activatedAt`** | Re-stamp `_activatedAt` when a timer is chosen | Re-stamping would fix the expiry maths but corrupt the "Active for" display, which legitimately measures from activation. Two clocks, two fields. |
| **Pomodoro releases only the session it acquired (`_pomHeldCaffeine`)** | Reference-count keep-awake requests; check the toggle state on release | A counter is overkill for exactly two possible owners (user, Pomodoro). Checking the toggle was the original approach and is what caused the bug — the toggle can change mid-session, and it says nothing about *who* turned caffeine on. |
| **Notes live in `%AppData%\Caffeine\notes.json`, not the registry** | Registry blob (keeps the "no files" rule); JSON next to the exe (portable) | Notes are user documents, not preferences — the registry is the wrong shape for growing content, and a single-file exe's own folder is not a guaranteed writable home. `%AppData%` is per-user, always writable, and roams with the profile. This is a deliberate, owner-approved departure from the registry-only rule, which now covers settings only. |
| **Notes is a real tab that grows the window; the separate window is opt-in** | Notes segment as a launcher button that always opens a window (the original build) | A tab that opens a window instead of switching panels behaves unlike its neighbours, and the app already had a perfectly good transform in `AnimateToPanel`. Selecting Notes now feels exactly like selecting Pomodoro, just with the window easing out to a size an editor needs. The window is then one click away for people who want it beside other apps. |
| **One `NotesView`, reparented between hosts** | A `NotesView` per host; recreate on each switch | Two instances would mean two `NotesStore`s over one file — the corruption hazard this document already warns about. One instance also preserves selection, scroll and unsaved keystrokes across a pop-out for free. Cost: hosts must not assume `Loaded` fires once, hence the `_initialised` guard. |
| **The Notes tab is disabled while popped out** | Let the tab steal the view back; raise the window instead; keep a second copy | Stealing empties the open window; silently raising a window when someone clicked a tab is surprising. A disabled tab with a tooltip says plainly where Notes went, and `OnNotesWindowClosed` guarantees it comes back however that window is dismissed. |
| **`WindowChrome` *and* `AllowsTransparency` for the notes window** | `WindowChrome` alone (square corners); `AllowsTransparency` alone (no resize); DWM `DWMWA_WINDOW_CORNER_PREFERENCE` | Neither alone gives a resizable window with the tray window's 12px corners and shadow. `WindowChrome` supplies dragging, snapping and edge resizing; `AllowsTransparency` supplies the rounded shell. DWM rounding was the alternative but its radius is OS-controlled (~8px) and would not match. Verified: resize, maximise-to-work-area and restore all behave. |
| **Panes round their own outer corners** | `ClipToBounds` on the shell; an `OpacityMask` | `ClipToBounds` clips to a *rectangle* in WPF, not to a corner radius — a classic trap. Letting the two panes carry `0,0,0,12` and `0,0,12,0` is exact and costs nothing; an opacity mask would need resizing logic. |
| **`SmoothScroller` is per-`ScrollViewer`** | Copy `MainWindow.AnimateScroll`'s shared `_scrollTarget` field | The tray window's version shares one target across every viewer, so scrolling one pane mid-animation disturbs another. Rather than replicate a known bug, Notes owns a small class keyed by viewer — the shape to port back to `MainWindow`. |
| **Rich text with a separate title field** | Plain text with the first line as the title (what this previously did); RTF instead of `XamlPackage` | Formatting and pictures were asked for, and both make "the first line is the title" untenable — a heading-styled first line is not the same thing as a title, and an image-only note has no first line at all. `XamlPackage` over RTF because it is the one WPF format that carries images *inside* the document; RTF round-trips them poorly. |
| **An index file plus one body file per note** | Everything in `notes.json` (bodies base64-encoded inline) | The index is rewritten on every debounce; bodies are not. Inlining a base64 screenshot would mean re-serialising megabytes on each keystroke pause and would make `notes.json` unreadable. Splitting keeps the index small and human-inspectable, and means a body write only happens when that body actually changed. Cost: two things to keep in step, so permanent deletion removes the body file and duplication copies it. |
| **A plain-text mirror in the index** | Open every document to search or draw a preview | Search and the list must not have to deserialise a `FlowDocument` per note. The mirror duplicates the text, which is the accepted cost — it is also the fallback that rebuilds a body whose file is missing, and the migration path off the old format. |
| **`ListBox` + `CollectionViewSource` — the one bound collection in the app** | Hand-built rows in a `StackPanel`, imperatively kept in sync | Grouping, filtering, sorting and templated selection states are precisely what `CollectionView` exists for. Hand-rolling them would be more code and more bugs. This does not reopen the wider MVVM question: there is still no view-model layer, and `NotesWindow` remains imperative code-behind. |
| **The tab strip moves: its own centred row on most panels, the title bar on Notes** | Title bar everywhere; its own row everywhere | Notes is the only panel that needs the extra row of height for an editor, and it is the only one wide enough (900px) to seat a strip beside three buttons — a centred strip cannot coexist with right-aligned buttons at 380px. So the strip stays exactly where it always was on Caffeine, Pomodoro and Settings, and `PlaceTabStrip` reparents it into the title bar only for Notes. There it is left-aligned at 14px, which is also the notes card's margin, so the two edges line up. That is why the title bar's left padding is 14 and not 20. |
| **No window title text** | Keep the "Caffeine"/"Pomodoro"/"Notes" label and its swap animation | The checked tab already names the view, so the label only repeated it — and on Notes, where the strip shares the title bar, there is no room for both. The title's shrink-swap-grow animation went with it; the strip's gooey indicator now carries that motion. |
| **Notes gets its own window ambience (`NotesAmbient`, a warm coffee), not its amber accent** | Tint the window amber to match the accent; leave Notes on the plain window colour | Amber is already the selection fill — a saturated amber field would swallow the very thing it needs to contrast against. A muted coffee sits at the same distance from neutral as `PomodoroRed` does, reads as the app's own subject matter, and leaves the amber free to do its job. It also forced a good change: the editor became a card so body text never sits on the tint, which made the two panes a matched pair. `NotesWindow` adopts the same tint so Notes looks like itself wherever it is hosted. |
| **The notes list is a floating card, selection is one travelling indicator** | Full-height pane with a per-row selected fill (the original) | The card reuses the tab strip's own fill and radius, so the two read as the same kind of object, and a single indicator that stretches between rows is literally the tab animation rotated 90°. Per-row fills cannot produce that motion — each row can only fade its own background. |
| **Order settles on discrete events, never while typing** | Enable `IsLiveSortingRequested` | Live sorting would yank the row you are editing to the top of the list mid-sentence. Titles and previews still update live; only position waits. WPF's default (live sorting off) is the desired behaviour, so this is a decision to *not* add code. |
| **Delete moves to Recently Deleted; only permanent deletion confirms** | Confirmation on every delete (what this previously did); immediate delete with an Undo toast | A confirmation dialog *is* the undo mechanism when there is no bin. With one, interrupting a reversible action is just friction — so the dialog moved to where it earns its place: the delete that cannot be undone. Retention is enforced on load rather than by a timer, so there is no background work and no clock to get wrong. |
| **Silently discard blank notes** | Keep them as `New Note` rows; prompt | Matches Apple Notes and keeps the list free of placeholders. Safe because it only ever discards a note whose body is whitespace — nothing authored is at risk. |
| **Atomic save + quarantine on corrupt read** | Write in place; start empty on parse failure | Notes are the only irreplaceable state in the app. Writing through a `.tmp` then `File.Move` means a crash mid-write cannot tear the file, and a file that fails to parse is renamed aside rather than silently overwritten by the next save. |
| **Todo is a fifth panel with its own ambience (`TodoAmbient`, a muted green), built exactly like Notes** | A separate app; a section inside Notes | Every feature in this app owns the window while it is showing, and Todo is no exception. Green is the obvious remaining colour and it means *done* — it also sits at the same distance from neutral as `PomodoroRed` and `NotesAmbient`, so the three read as siblings. Reusing the Notes shape (one shared view, reparented, one store, floating cards at identical geometry) meant the second feature cost far less than the first and cannot drift from it visually. |
| **Task rows are built in code; only the lists sidebar is bound** | An `ItemsControl` with a `DataTemplate` and an `IsExpanded` flag on `TodoTask` | A row changes shape with its content — chips appear, a detail panel unfolds, it can be dragged — and a template would need converters for every one of those plus a UI concern stored on the model. `BuildRow` is one method you can read top to bottom. The sidebar stays bound because it really is just a list. Notes' `CollectionViewSource` is still the only bound *collection view*; this does not widen that. |
| **Deleting a task is undoable, not confirmed; deleting a list is confirmed** | Confirm both; a Recently Deleted bin for tasks | A task is cheap to retype and expensive to interrupt, so it gets a six-second Undo toast and the disk write waits for that window to close. A list takes its tasks with it, so it asks. A bin would be a third store to keep in step for data with none of a note's weight. |
| **Todo preferences are read live from the registry through `TodoSettings`** | Load into fields on startup, as everything else does | Settings and the view are two surfaces over the same four values, and the app already has one bug-shaped precedent for hand-syncing duplicated settings (`StayGreenToggle` / `CaffeineStayGreenToggle`). A static that reads the key on access cannot go out of step; the values are tiny and read at most a few times per rebuild. |
| **Reminders live in `App`, not `TodoView`** | Check on the view's own timer | A task must announce itself whether or not the tab has ever been opened, and the view is created lazily on first use. A 30-second `DispatcherTimer` in `App` is the only place with that guarantee. `TodoTask.Notified` keeps it to once per due date. |
| **Recurrence is five presets, applied on completion** | Full RRULE-style custom rules | Google Tasks itself offers little more, and a rules engine would be the largest thing in the codebase for a feature that is nearly always "every week". Advancing the due date on completion — rather than spawning a new task — keeps one row, one history, and no duplicates to clean up. |
| **A prompt set is a note *kind*, not a sixth panel** | Its own tab and view beside Notes and Todo; a filter or folder inside Notes | A prompt set is a note that happens to hold a queue instead of prose — it wants the same list, the same search, the same pin and the same 30-day bin. Making it `Note.Kind` meant the list, the store, the debounce, the bin and blank-discard were all *existing* code that needed no changes at all; only the editor pane's body swaps. A sixth tab would not have fitted the strip either (the fifth was already the documented limit), and folders are explicitly out of scope. |
| **Prompts in `prompts\<id>.json`, one file per note** | Inline in `notes.json`; reuse the `bodies\` `XamlPackage` | Exactly the split `bodies\` already justifies: the index is rewritten on every debounce, and a set of twenty long prompts inlined there would be re-serialised on every keystroke's pause and make `notes.json` unreadable. A `XamlPackage` was wrong for the same reason it is right for documents — there is nothing binary or formatted in a prompt, and JSON stays inspectable. Cost: a third file kind to keep in step, so duplication copies it and both deletions remove it. |
| **The array's order is the model's order — no `Order` field, no `Id`** | An `Order` int per prompt, as `TodoTask` has; sort Sent by `SentAt` | `TaskList` needs `Order` because tasks move between lists and sort modes. A prompt set is one ordered list partitioned in two, where every operation is a move within it — so sending is `Remove` then `Add`, and JSON arrays already preserve exactly that. It removes a whole class of "the order field disagrees with the list" bug, and `NormalisePrompts` re-establishes the invariant in one place on load. |
| **Copy is inert; the tick is what marks sent** | Copy also marks it sent (one click for the whole loop); copy marks sent with an Undo toast | Owner's call. Copying a prompt to edit it elsewhere, or to re-send an old one, must not silently move it — and a Copy that sometimes mutates and sometimes does not (on a Sent card) is two behaviours behind one icon. The icon flashing to a tick for 1.2 s is the acknowledgement instead. |
| **`ScrollingTextBlock` as a hand-rolled `UserControl`** | Plain `TextBlock`; an animation library | Per-character odometer transitions are the app's signature motion. Diffing only changed characters keeps digit-by-digit clock updates cheap. |

---

## Configuration & Persistence

_This project has **no environment variables** and no config file. There are two stores, split by what the data *is*: **settings** go to the registry, **user content** goes to a file. Registry writes are user-level only — never write to `HKLM`._

### Settings — registry

| Key | Value | Type | Written by | Notes |
| --- | ----- | ---- | ---------- | ----- |
| `HKCU\Software\CaffeineWin` | `StayGreenMode` | DWORD `0`/`1` | `App.SaveStayGreenPreference` | Read once in `OnStartup` |
| `HKCU\Software\CaffeineWin` | `Theme` | String `System`\|`Light`\|`Dark` | `ThemeManager.SavePreference` | Parsed with `Enum.TryParse`; unknown values fall back to `System` |
| `HKCU\Software\CaffeineWin` | `NotesBounds` | String `x,y,w,h,maximised` | `NotesWindow.PersistGeometry` | Written on close. Uses `RestoreBounds` when maximised so un-maximising returns to a sane size. Ignored unless the rect still intersects an attached monitor's working area |
| `HKCU\Software\CaffeineWin` | `NotesListWidth` | DWORD | `NotesWindow` on splitter drag and on close | Clamped to 200–420 on read; out-of-range values are ignored, not corrected |
| `HKCU\Software\CaffeineWin` | `NotesSelectedId` | String (`Note.Id`) | `NotesWindow.PersistGeometry` | Falls back to the newest note if that id is gone |
| `HKCU\Software\CaffeineWin` | `NotesFormatBar` | DWORD `0`/`1` | `NotesView.FormatToggle_Click` | Whether the formatting bar is folded out. Collapsed by default |
| `HKCU\Software\CaffeineWin` | `TodoBounds` | String `x,y,w,h,maximised` | `TodoWindow.PersistGeometry` | Same rules as `NotesBounds` |
| `HKCU\Software\CaffeineWin` | `TodoSidebarWidth` | DWORD | `TodoView.PersistState` | Clamped to the column's 170–330 range on read |
| `HKCU\Software\CaffeineWin` | `TodoSelectedList` | String (`TaskList.Id`) | `TodoView.PersistState` | The list to reopen on. Falls back to the first list if that id is gone |
| `HKCU\Software\CaffeineWin` | `TodoSort` | DWORD (`TaskSort`) | `TodoSettings.Sort` | `0` My order · `1` Date · `2` Title |
| `HKCU\Software\CaffeineWin` | `TodoDensity` | DWORD (`TaskDensity`) | `TodoSettings.Density` | `0` Comfortable · `1` Compact |
| `HKCU\Software\CaffeineWin` | `TodoCompletedOpen` | DWORD `0`/`1` | `TodoSettings.CompletedOpen` | Whether the Completed section starts expanded. Default on |
| `HKCU\Software\CaffeineWin` | `TodoDueHour` · `TodoDueMinute` | DWORD | `TodoSettings` | When a dated task with no time falls due. Default 09:00 |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | `CaffeineWin` | String — `Environment.ProcessPath` | `MainWindow.SetAutoStart` | Autostart; deleted (not zeroed) when disabled. Points at wherever the exe was when the toggle was flipped — moving the exe silently breaks it. |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` | `AppsUseLightTheme` | DWORD (**read-only**) | — | `0` means dark; consulted only when the setting is `System` |

`Reset to Defaults` restores theme (System), autostart (off), Stay Green (off), all Pomodoro settings (25/5/15, 4 cycles, keep-awake on), and the four Todo preferences (Comfortable, My order, Completed expanded, 09:00) — but it does **not** deactivate a running keep-awake session, and it deliberately does not touch notes, tasks, or either feature window's geometry.

### User content — file

| Path | Format | Written by | Notes |
| ---- | ------ | ---------- | ----- |
| `%AppData%\Caffeine\notes.json` | Indented JSON array of `Note` — the **index** only | `NotesStore.Save` | Titles, plain-text mirrors, timestamps, pin and bin state. Written on every create, delete, pin, duplicate, selection change, window close, app exit, and 600 ms after typing stops |
| `%AppData%\Caffeine\bodies\<id>.xamlpkg` | WPF `XamlPackage` | `NotesStore.SaveBody` | One per note: the formatted body with any pictures packaged inside it. Written only when the body is actually dirty, so a 2 MB screenshot is not re-serialised on every keystroke's debounce |
| `%AppData%\Caffeine\prompts\<id>.json` | JSON array of `Prompt` | `NotesStore.SavePrompts` | One per **prompt** note, in place of a body file — a note has one or the other, never both. Array order is the on-screen order; unsent entries first. Written only when the set is dirty |
| `%AppData%\Caffeine\tasks.json` | Indented JSON `{ Lists, Tasks }` | `TodoStore.Save` | Both collections in one snapshot — they are always read and written together, and a task without its list is meaningless. Written on every create, edit, complete, reorder, list change, and when an undo window closes |
| `%AppData%\Caffeine\*.tmp` | — | all writers | Transient. Written first, then `File.Move(overwrite: true)` — a crash mid-write cannot tear the real file |
| `%AppData%\Caffeine\notes.corrupt-<stamp>.json` | — | `NotesStore.Quarantine` | Only on a `JsonException`. The unreadable file is renamed aside so the next save cannot destroy it, and the failure is surfaced in the window |
| `%AppData%\Caffeine\tasks.corrupt-<stamp>.json` | — | `TodoStore.Quarantine` | Same rule for tasks |
| `%AppData%\Caffeine\prompts\<id>.corrupt-<stamp>.json` | — | `NotesStore.QuarantinePrompts` | Same rule per prompt set. Only a *parse* failure quarantines; a read failure instead returns `null` and blocks writes for that note, which is what stops a save emptying a file we could not open |

The app must start correctly with none of this present: a missing index means an empty list and the "No Notes" state, a missing *body* file falls back to rebuilding the document from the note's plain-text mirror — which is also how notes written before rich text get their first body file — and a missing *prompt* file means an empty queue with an `Add prompt` row.

---

## External Integrations

No network calls, no telemetry, no accounts, no third-party services. The only external surface is the operating system.

| Surface | Purpose | Mechanism | Failure mode |
| ------- | ------- | --------- | ------------ |
| `kernel32!SetThreadExecutionState` | Suppress display sleep | P/Invoke, `ES_CONTINUOUS \| ES_DISPLAY_REQUIRED` | Return value is ignored; a failure is silent and the screen simply sleeps. Group Policy or another app's request can also override the outcome. |
| `user32!SendInput` | 1px cursor nudge for Stay Green | P/Invoke, `MOUSEEVENTF_MOVE` | Return value ignored. Blocked by UIPI when a higher-integrity window (UAC prompt, some full-screen games, secure desktop) has focus — presence silently lapses. |
| Windows Registry (HKCU) | Preferences, autostart, notes window state | `Microsoft.Win32.Registry` | `SetAutoStart` returns silently if the `Run` key can't be opened writable; preference reads use `?.` and fall back to defaults. |
| File system (`%AppData%`) | Notes storage | `System.IO` + `System.Text.Json` | `IOException`/`UnauthorizedAccessException` are caught, recorded in `NotesStore.LastError`, and shown as a red strip in the window — a failed save is never silent. A corrupt file is quarantined rather than overwritten. |
| `WinForms.Screen.AllScreens` | Validating restored window bounds | WinForms | A saved position on a detached monitor is discarded and the window centres instead. |
| `SystemEvents.UserPreferenceChanged` | Follow OS light/dark switches | Event, filtered to `UserPreferenceCategory.General` | Fires on a non-UI thread — the handler marshals via `Dispatcher.BeginInvoke`. Must be unhooked in `Quit()` or the process is pinned. |
| WinForms `NotifyIcon` | Tray icon, menu, balloon tips | `ShowBalloonTip(3000)` | Balloons are suppressed entirely when Windows Focus Assist is on — phase completion then only beeps. |
| `Console.Beep` | Pomodoro chime | 6 beeps in two triads, ~1.8 s total, on a thread-pool thread | Silent on machines with no beep-capable output path; not user-mutable. |
| GDI+ (`Bitmap.GetHicon`) | Runtime icon rasterisation | `System.Drawing` | Each call allocates an unmanaged icon handle that is currently never released (see Technical Debt). |

---

## Testing Strategy

**Current state: there is no test project, and CI does not run tests.** `build.yml` restores, builds Release, publishes, and uploads the artifact — a compile check only. `CONTRIBUTING.md` asks contributors to build with zero warnings and verify manually.

This is a deliberate consequence of the architecture, not an oversight to paper over: the timing logic, the Pomodoro machine, and the state transitions all live in code-behind coupled to named XAML elements, so nothing is reachable from a test host today. **Anything genuinely testable should be extracted first rather than tested through the UI.** The natural first candidates, in order: `FormatTime`, `PomAdvancePhase`, the auto-off elapsed comparison, and the progress-arc geometry maths — all pure functions trapped inside `MainWindow`.

`Prompt.FormatSentAt`, `TodoTask.FormatDue` and `TodoTask.NextOccurrence`, and every query on `TodoStore`, are pure and parameterised the same way — they take `now` rather than reading the clock. `Note`'s four `static` derivation methods are the same exception: `DeriveTitle`, `DerivePreview`, `FormatListTimestamp` and `FormatEditorTimestamp` take their inputs as parameters (including `now`, so the timestamp cases are deterministic) and touch no UI. If a test project is ever added, start there — it is the only logic in the codebase already shaped for it.

Until then, treat this as the manual regression checklist for any change that touches state:

1. Tray left-click toggles; icon turns blue with steam and the tooltip tracks Active/Inactive.
2. Power button and tray click stay in agreement in both directions.
3. Auto-off, inactive start: pick 15m while inactive → activates; label counts down; fires and deactivates; **the pill snaps back to Off when it fires**.
4. Auto-off, mid-session: activate, leave it running longer than the window you're about to pick, then pick 15m → it must count down a fresh 15m, not expire on the next tick. Then set it back to Off → "Active for" must still show total time since activation.
5. Stay Green: toggle on while active → cursor nudges once per second and never drifts; the power request is released.
6. Both Stay Green toggles (Caffeine panel and Settings) stay mirrored.
7. Theme: switch Light/Dark/System; switch the OS theme while on System; confirm the animated surfaces (window background, title, power button) follow — these are the ones that break. Check the Settings text inputs in both themes too: pick a Pomodoro **Custom** duration and look at the Todo default due time. Both must be rounded app-coloured fields with an accent ring on focus, never a white box with a system border.
8. Navigation: Caffeine ↔ Pomodoro ↔ Settings, plus `Escape` from Settings and from a tab; spam the tabs mid-animation and confirm no stuck panel.
9. Pomodoro: Start/Pause/Resume/Reset/Skip; run a phase to completion and check balloon, beeps, phase advance, cycle counter, and that keep-awake drops.
10. Pomodoro ownership: turn caffeine on **by hand**, then run a work phase to completion (or Reset/Skip it) with "Keep screen awake" on → **caffeine must stay on**. Repeat with caffeine off beforehand → Pomodoro turns it on and must turn it back off.
11. Autostart on → reboot → app launches from the tray.
12. Tray → Exit → process actually terminates and the execution-state request is released.
13. Notes — opening inline: selecting the `Notes` tab eases the window out to 900×620 and cross-fades the notes UI in, with the gooey indicator landing on Notes. Switching to Caffeine or Pomodoro eases it back to 380×500. The pop-out button appears in the title bar **only** on the Notes tab.
13a. Notes — pop out and dock: pop out → a separate window opens, the tray window shrinks back to Caffeine, and the Notes tab greys out. Dock → the window closes and Notes reappears inline on its tab. Do it with a half-typed note and confirm the text, the selected note and the scroll position all survive both directions.
13b. Notes — closing the popped-out window with **✕** or Escape rather than docking must also re-enable the Notes tab; selecting it then has to show the notes inline again.
13c. Notes — drag the tray window by its title bar *after* an expand/shrink cycle. If it will not move, an animation is still holding `Left`/`Top`.
14. Notes — editing: type into a note and watch the row's title and preview update live **without the row changing position**; stop typing and confirm the timestamp updates ~600 ms later.
15. Notes — lifecycle: create, pin (confirm `PINNED`/`NOTES` headers appear and vanish with the last unpin), duplicate, delete via both the toolbar and the right-click menu, and cancel a delete.
16. Notes — blank discard: create a note, type nothing, select another → the blank one disappears.
17. Notes — search: filter, confirm "No matches" on a miss, clear it, and confirm `Ctrl+N` while filtered clears the search so the new note is visible.
18. Notes — persistence: resize the window, drag the divider, select a note, close and reopen → all three come back. Then edit a note and exit from the **tray** without closing the notes window; reopen and confirm the edit survived.
19. Notes — theme: switch light/dark with the window open and confirm the list, editor, selection and pin glyphs all follow.
20. Notes — resilience: with the window closed, corrupt `notes.json` (e.g. truncate it), reopen → the app must start, show the red error toast, and leave a `notes.corrupt-*.json` alongside rather than destroying the original.
21. Notes — shell: all four corners rounded with a soft shadow gutter; **maximise** → corners flatten with no transparent gap and the taskbar stays visible; **restore** → corners and gutter return. Drag each edge and corner to resize, drag the title bar to move, and drag to a screen edge to snap.
21b. The mark: tray icon is a grey cup when idle and a blue steaming cup when active, and both look centred at 16/20/32px; the taskbar and Alt-Tab icon match; the Caffeine tab shows a steaming cup at all times. The big toggle shows the cup alone when off — grey, no handle, dead centre in the circle — and when on it turns white-on-blue and **two wisps rise and fade continuously**, with the cup settling a little lower to make room. Turn it off again and the steam must fade out and leave nothing parked mid-drift. Toggle caffeine ~20 times and confirm handle count in Task Manager stays flat — that path used to leak an icon handle per toggle.
21c. The tab strip: centred in its own row on Caffeine, Pomodoro and Settings; in the title bar on the wide panels, where its **left edge lines up with the floating card below it**. Switch through all four tabs and confirm the gooey indicator lands **around the icon *and* its label**, not the icon alone — the label is revealed by a template trigger that runs after the `Checked` event, so anything measuring synchronously will size to the icon. Check it in both strip positions and on the red, coffee and green backgrounds.
21f. Rich text round-trip — the one that has already caught a bug. Type three lines, then apply a **heading** to one, **italic** to another and a **bullet** to a third; paste a screenshot in. Restart the app and confirm every one of those came back, and that the picture is still there. Watch `%AppData%\Caffeine\bodies\*.xamlpkg` while you do it: applying formatting must change the file size, not just typing.
21k. Ambience: selecting Notes eases the whole window to a warm coffee and back out again on leaving, the same way Pomodoro does with red — check both themes, and check the popped-out window carries the same tint. The amber selection must still read clearly against the list card, and body text must never end up sitting on the tint.
21j. The formatting bar: collapsed by default, opens **above** the title from the `Aa` toggle beside the date, remembers its state across a restart, and folds itself away on entering Recently Deleted. With it collapsed, the body should start close under the title — no dead band.
21g. Title and body are separate: the title field is its own line above the divider, Enter or Tab in it drops into the body, and the list row shows the title with the body's *first* line as preview. A note with only a picture and no text must survive being navigated away from — that is `HasImages` doing its job.
21l. The picture viewer: click a picture in a note and it opens fitted, with the dimensions bottom-left and the zoom bar bottom-centre. Zoom with the buttons, the wheel (which should keep the point under the pointer put), and `+`/`−`; `0` fits, `1` goes to 100%; drag to pan once it overflows; double-click flips fit/actual. Escape, the ✕ and a click on the surround all dismiss, and the editor must be usable again afterwards. Clicking just *beside* a picture should place the caret, not open the viewer.
21h. Pictures three ways: the toolbar button, Ctrl+V of a screenshot, and dragging a PNG onto the editor. Copying rich text from a browser should still paste as *text*, not silently drop it in favour of an image.
21i. Migration: put a pre-rich-text `notes.json` (notes with a `Body` string and no `Title`) in place and launch. The first line must become the title, the rest the body, and the `Body` field must be gone from the rewritten file.
21e. Recently Deleted: delete a note → it leaves the list with no prompt and the footer shows a count. Open the bin → rows show a `30 days left` countdown, new-note and pin are gone, Restore is present, and the editor is read-only. Restore → the note is back and the footer disappears once the bin is empty. Delete from inside the bin → this time it *does* confirm, and afterwards the note is gone from `notes.json`. Pin a note, delete it, restore it, and confirm it is no longer pinned.
21d. Notes selection: the list is a floating rounded card matching the tab strip's fill; switching notes stretches one indicator across both rows before settling, and it takes the height of the target row (compare a one-line preview against a two-line one). Scroll the list and confirm the indicator stays glued to its row. Type until a preview wraps to a second line and confirm the indicator resizes to match.
21a. Notes — family resemblance: put the two windows side by side. Title size, title-bar button circles, surface colours, corner radii and hover behaviour should be indistinguishable between them. The minimise, maximise and close glyphs must render as `─ □ ✕` (and `❐` when maximised), not as missing-glyph boxes.
23. Todo — opening inline: selecting the `Todo` tab eases the window out to the wide size and cross-fades the todo UI in on the green ambience, with the gooey indicator landing on Todo and the tab strip moving into the title bar, its left edge aligned with the lists card. Leaving it eases back. **Then open Settings from Todo and click the Todo tab again — it must return to Todo, not Caffeine** (that path has its own handler and had exactly this bug).
23a. Todo — pop out and dock, and close the popped-out window with ✕: same expectations as 13a/13b, including the Todo tab greying out and coming back.
23b. Todo — lists: create a list (it should drop straight into rename), rename it, recolour it from the swatch menu, move it up and down, and delete it. Deleting the *last* list must be refused with a message rather than leaving the app with none. Deleting a list with tasks must say how many will go.
23c. Todo — the task lifecycle: add from the top row with Enter; click the body to expand exactly one row at a time; type details; add a subtask (it should open for naming, and an unnamed one must vanish when you click away); tick the parent and confirm the subtask completes too; check the `0/1` counter.
23d. Todo — due dates: open the picker on a task with **no** date and on one that **already has** one. Both must open — populating the calendar used to rebuild the list and pull the popup's anchor away. Pick Today, Tomorrow, a calendar day, add a time, then No date. The chip and the row's due label must agree, and an overdue task's chip must turn red.
23e. Todo — repeat: set a due date and a weekly repeat, then complete the task. It must **stay in the list** with the date advanced by a week, not move to Completed.
23f. Todo — completed section: tick a task → it moves under `Completed (n)` with a filled tick and strikethrough. Collapse and expand the section, and confirm the state survives a restart. `Clear all` removes them, offering an Undo.
23g. Todo — delete and undo: delete a task from the row menu and from the detail's Delete. The toast must offer Undo, restore the task (and its subtasks) when clicked, and — if left alone — the deletion must be on disk after the toast fades.
23h. Todo — right-click: Rename, Move to ▸ another list (the subtasks must follow), Duplicate, Add subtask, Delete. On a subtask: Rename, Make a task, Delete.
23i. Todo — sorting and dragging: in `My order`, drag a task above and below its neighbours and confirm the order survives a restart. Switch to Date and Title and confirm both order correctly with undated tasks last — and that dragging does nothing in those modes.
23j. Todo — reminders: give a task a due time a minute or two out and leave the app on any tab, or closed to the tray. Within 30 s of the deadline a tray balloon must appear, exactly once. Change the date and confirm it can fire again.
23k. Todo — settings: change row density, sort and default due time, and toggle the Completed default. Each must be reflected in the list without reopening the tab. `Reset to Defaults` must restore all four **and leave your tasks and lists alone**.
23l. Todo — the retemplated controls: the calendar must be dark, with today in green, adjacent-month days dimmed, working ‹ › arrows and a month/year drill-up. Right-click menus must be dark with a subtle separator and a chevron on `Move to`. Nothing may flash white.
23m. Todo — resilience: corrupt `tasks.json` and relaunch. The app must start, show the error, and leave `tasks.corrupt-*.json` beside it. Separately, delete `%AppData%\Caffeine` entirely and launch → first run must seed a single `My Tasks` list and work.
24. Prompt sets — the shape: click `P` beside `+`. The title must ask for an **Application name**, the `Aa` toggle must be gone, and the body must show `PROMPTS TO SEND (1)` over one empty card with a `Type or paste a prompt…` watermark. Switch to an ordinary note and confirm the formatting bar and its toggle come *back* — the bar is collapsed, not merely folded, on a prompt note, so Tab must not reach B/I/U there either.
24a. Prompt sets — the loop: type a prompt, add two more from `+ Add prompt`, and confirm they number 1, 2, 3. Tick the middle one → it leaves the queue, the remaining two renumber to 1 and 2, and it appears under `SENT (1)` with a `Sent today HH:mm` line. Tick two more and confirm Sent reads **oldest first**, newest at the bottom. Tick a sent one back and confirm it returns to the *bottom* of the queue, its stamp gone.
24b. Prompt sets — copy and delete: Copy puts the prompt on the clipboard, flashes the icon to a tick for about a second, and **does not** mark it sent. Delete removes the card and offers Undo for six seconds; click Undo and it comes back where it was. Let a second one expire and confirm it is gone from `prompts\<id>.json` on disk.
24c. Prompt sets — reordering: drag a card by its **grip** (the two bars at the left) above and below its neighbours; the numbers must resettle on drop and survive a restart. Dragging from the middle of a card must select text, not reorder. Sent cards have no grip.
24d. Prompt sets — the list row: the row shows a `P` badge before the title and `· 3/7 sent` beside the timestamp, both updating the instant you tick. Select it and confirm the badge inverts so it stays legible on the amber fill. A screen reader must announce "*name*, prompt set, 3 of 7 sent".
24e. Prompt sets — as a note: pin it, search for text inside one of its prompts (the plain-text mirror should find it), duplicate it (the copy must get its **own** `prompts\<id>.json`, so editing one must not change the other), delete it to Recently Deleted, and restore it. In the bin the cards must be read-only — no ticks, no delete buttons, no `Add prompt` — but Copy must still work.
24f. Prompt sets — blank discard: create one, type nothing at all, select another note → it must vanish, and leave **no** file behind in `prompts\`. Then create one, give it only an application name, and confirm it *survives* — a named set with no prompts yet is legitimate.
24g. Prompt sets — resilience: with the app closed, corrupt a `prompts\<id>.json`. Reopen that note → the error toast appears, the file is set aside as `<id>.corrupt-*.json`, and the note still opens. Separately, confirm an index written before this feature loads with every note reading as an ordinary text note.
24h. **The index round-trip — run this after ANY change to `NotesStore`'s serialiser.** Edit any note at all, quit from the tray, relaunch, open Notes. Every note must still be there and **no `notes.corrupt-*.json` may appear**. `Save` and `Load` must use the same `JsonSerializerOptions`; when they did not, `Note.Kind` was written as a string, failed to read back as a number, and the whole index was quarantined on the next launch.
24i. Prompt sets — the bin is genuinely read-only: note a prompt set's `PromptTotal`/`PromptSent` in `notes.json` and the modified time of its `prompts\<id>.json`, then delete it, open Recently Deleted, let it be selected, and leave the bin. Neither the file nor the counts may change.
24j. Prompt sets — no lost write on a refresh: type into a prompt card and, **within 600 ms**, type a search query the note does not match so it drops out of the list. Clear the search and reopen it — what you typed must be there. Any path that changes the active note without going through `LeaveActiveNote` has to commit first; `SyncActiveNoteToSelection` is the one that did not.
24k. Prompt sets — theme: with a prompt set open, switch Light ↔ Dark in Settings. The cards, section headers, ticks and grips must follow immediately. They are built in code, so their brushes are resolved once — only the `ThemeChanged` rebuild makes them move.
22. Notes — motion: the window eases up on open rather than snapping; switching notes cross-fades the editor; the selected row slides in from the left while the amber fill appears instantly; toolbar and dialog buttons scale on hover and squash on press; the wheel eases the list rather than stepping it. Nothing should be left stuck mid-animation — check for a half-faded editor after rapid clicking between notes.

---

## Deployment & Distribution

**Local development**

```powershell
dotnet build                # → bin\Debug\net8.0-windows\win-x64\caffeine-win.exe
dotnet run                  # csproj is at the repo root; there is no subfolder
```

**Release**

```powershell
dotnet publish -c Release -r win-x64 --self-contained
# → bin\Release\net8.0-windows\win-x64\publish\caffeine-win.exe  (single file, compressed)
```

**Pipeline** — `.github/workflows/build.yml` on push/PR to `main`, `windows-latest`, `actions/setup-dotnet@v4` pinned to `8.0.x`; restore → build Release → publish → upload `publish/` as artifact `caffeine-win`.

**Distribution** — the published exe is attached manually to a GitHub Release. There is no auto-update mechanism, no code signing (users will see a SmartScreen warning), and no installer. Version lives in exactly one place: `<Version>` in `caffeine-win.csproj`. Bump it there when releasing.

---

## Technical Debt

- [ ] **The mark's geometry exists twice** — as string constants in `App.xaml.cs` and as the `CupIcon` path in `MainWindow.xaml` — because XAML cannot bind path data to a C# constant. A `Geometry` resource in a shared dictionary would fix it along with the shared-styles item above.
- [ ] **`MainWindow.xaml.cs` is ~970 lines with four unrelated jobs** — panel/indicator animation, the Pomodoro machine, registry autostart, and window chrome. This is the root cause of the untestability above. Split before adding features here.
- [ ] **Swallowed exception.** `PomDrawProgressArc` wraps `Geometry.Parse` in `try { } catch { }`. Either the format string can't fail (drop the catch) or it can (then handle it) — the empty catch hides which.
- [ ] **Pomodoro settings are not persisted.** Durations and cycle count reset every launch while theme and Stay Green survive — an inconsistency users notice. Fix in the same `HKCU\Software\CaffeineWin` key.
- [ ] **Duplicated Stay Green toggle** (`StayGreenToggle` and `CaffeineStayGreenToggle`) hand-synced in two handlers. Two more surfaces would make this untenable.
- [ ] **Five near-identical pill-indicator wirings** and three copy-pasted `Pom*Custom_Checked` / `*_LostFocus` handler triplets. `PositionAllSettingsIndicators` already hints at the missing abstraction: a small `(indicator, transform, panel)` record per group.
- [ ] **Custom-duration inputs commit only on `LostFocus`** and silently ignore non-numeric or non-positive input with no feedback. No upper bound either. (They are at least styled now — see `SettingsInputStyle`.)
- [ ] **`TodoView.Rebuild()` re-creates every row on every change.** Fine at the sizes this holds (a few dozen rows), and it buys exactly one code path onto the screen — but a list of several hundred tasks would feel it, and it means the whole list loses focus and hover state on each edit.
- [ ] **`MainWindow`'s smooth scrolling shares one target across every `ScrollViewer`,** so scrolling one panel mid-animation in another interferes. `Controls/SmoothScroller.cs` is the corrected per-viewer version, now shared by both feature views; port it back and delete `MainWindow.AnimateScroll` and its `_scrollTarget`/`_scrollAnimating` fields. Both still lerp by a magic `0.2` per frame.
- [ ] **Native return values ignored.** Neither `SetThreadExecutionState` nor `SendInput` is checked, so a blocked request is indistinguishable from a working one — the UI still claims "Active".
- [ ] **Magic numbers in animation code** (durations, `0.95`/`0.97`/`1.12` scales, `cx/cy/r = 90,90,87`, `0.62`/`0.35` glyph widths). Fine individually; collectively they make motion tuning a search-and-replace exercise.
- [ ] **The circular title-bar button template exists three times** — inline in `MainWindow.xaml` for the gear, close and pop-out buttons, and once as `TitleBarButtonStyle` in `NotesWindow.xaml`. Easing resources are likewise declared per file. The right fix is a `Themes/Shared.xaml` merged in `App.xaml`; it keeps being deferred because it means restructuring a 757-line XAML file that no individual change has had reason to touch.
- [ ] **`NotesWindow.xaml.cs` guards re-entrancy with two boolean flags** (`_suppressSelectionChange`, `_suppressEditorChange`). They are correct but fragile — every new path that assigns `SelectedItem` or `Editor.Text` programmatically has to remember them. A small "programmatic update" scope helper would make this harder to get wrong.
- [ ] **Notes ordering is only correct after a refresh.** Positions settle on create/delete/pin/search/reopen but not while typing (deliberate — see Key Design Decisions). The consequence is that a long editing session leaves the list in a stale order until one of those events happens.
- [ ] **`TodoView` builds its rows with `(Brush)FindResource(...)` and never rebuilds on a theme change.** Brushes resolved in code are a one-time lookup, so a Light↔Dark switch leaves an open task list on the old palette until the next edit happens to call `Rebuild()`. `NotesView` now subscribes to `ThemeManager.ThemeChanged` for exactly this reason (`RebuildPrompts`); `TodoView` wants the same four lines. `ThemeChanged` otherwise has a single subscriber in the whole repo.
- [ ] **A discarded blank note can orphan its body file.** `LeaveActiveNote` removes a note whose body it has already written during the debounce — type into a new note, clear it, navigate away, and `bodies\<id>.xamlpkg` stays behind with no index entry pointing at it. The prompt path handles this (`DeletePrompts` on discard); the document path predates it and does not. One line in the same place fixes it.
- [ ] **No tests, no analyzers, no CI check beyond compilation.**

---

## Known Bugs

- [ ] **Autostart entry goes stale when the exe moves.** The `Run` value records `Environment.ProcessPath` at toggle time; moving or replacing the exe leaves a dead entry, and the Settings toggle still reads as enabled because it only checks that *a* value exists.
- [ ] **Stay Green silently lapses at the secure desktop.** `SendInput` is dropped while a UAC prompt or other higher-integrity window holds focus; the UI keeps reporting "Active".
- [ ] **Nothing prevents a second instance.** Two copies mean two tray icons and two competing execution-state requests.
_(The `ScrollingTextBlock` accessible-text doubling recorded here was fixed on 2026-07-29 — see the changelog.)_

---

## Out of Scope

- [ ] Cross-platform support. `net8.0-windows`, WPF, WinForms, GDI+, the registry, and both P/Invokes are all Windows-only, by choice.
- [ ] Keeping the *session* awake (`ES_SYSTEM_REQUIRED`) or defeating a policy-enforced lock screen. Caffeine suppresses display sleep; it is not a policy-bypass tool.
- [ ] Installer, MSIX packaging, auto-update, code signing.
- [ ] Telemetry, analytics, crash reporting, or any network call whatsoever.
- [ ] Global hotkeys, CLI arguments, scripting/automation surface.
- [ ] Pomodoro history or statistics — the timer is a timer. It has no connection to Todo, and starting a work phase does not pick up a task.
- [ ] Notes: **folders or categories** — notes remain a single flat list. (Rich text and Recently Deleted were both in this list and are now built.)
- [ ] Notes: **checklists inside a document**, **tables**, text **colour/highlight**, a monospaced style, and link detection. The formatting set stops at bold/italic/underline/strikethrough, a heading style, and bulleted/numbered lists. (A *prompt set* is a checklist of sorts, but it is a separate note **kind** with its own storage rather than a tickable paragraph in a `FlowDocument` — the thing this line rules out.)
- [ ] Prompt sets: **variables or placeholders** in a prompt, templates, sending to an AI provider, or any other integration. Caffeine puts text on the clipboard; what happens next is not its business — and a network call would break the app's central promise.
- [ ] Prompt sets: **more than one queue per note**, nesting, tags, or a cross-note view of every unsent prompt. A prompt set is one application's queue, and the note list is the index of applications.
- [ ] Notes: attachments other than pictures (PDFs, arbitrary files), and any editing of an inserted picture beyond the automatic width limit.
- [ ] Notes: an **Empty Bin** action that clears Recently Deleted in one go — deliberately left out; notes purge themselves after 30 days and can be deleted individually.
- [ ] Notes: sync, sharing, export, attachments, images, or a user-selectable sort order.
- [ ] Todo: **sync with Google Tasks** or anything else, sharing, and import/export. The store is a local JSON file and stays one.
- [ ] Todo: **custom recurrence rules** (every 3rd Tuesday, weekday-only, end-after-n). Five presets only.
- [ ] Todo: **subtasks more than one level deep**, and subtasks with their own due dates — same limits Google Tasks has.
- [ ] Todo: a **starred/priority** flag, tags, an all-lists combined view, a calendar view, and per-list sort. Sorting is one global mode.
- [ ] Todo: a **Recently Deleted bin**. Deletion is undoable for six seconds and then final — a task is not a note.
- [ ] Todo: **snooze**, repeat reminders, or any notification beyond the single tray balloon a task gets when it falls due.
- [ ] Localisation. UI strings are inline English literals.
- [ ] Per-monitor DPI polish beyond WPF's defaults; the tray window is fixed at 380×500 (900×620 on the wide panels) and non-resizable — only the popped-out Notes and Todo windows resize.

---

## Changelog

| Date | Change |
| ---- | ------ |
| 2026-05-21 | Initial commit: keep-awake app with system tray, Pomodoro timer, and Stay Green mode |
| 2026-05-21 | Added dark/light theme support with system theme detection (`ThemeManager`, `Themes/`) |
| 2026-05-21 | Added `ScrollingTextBlock` control for odometer-style digit animation |
| 2026-05-21 | Added open-source files: README, MIT licence, contributing guide, CI workflow, GitHub templates |
| 2026-05-21 | Enabled single-file compression for smaller release builds |
| 2026-05-21 | Added Pomodoro sound, fixed settings navigation, smooth scroll, show window on launch |
| 2026-05-21 | Bumped version to 1.1.0 |
| 2026-07-29 | Added ARCHITECTURE.md and CLAUDE.md; documented state model, registry keys, OS integrations, design decisions, and an audit of existing debt and bugs. No behavioural changes. |
| 2026-07-29 | Fixed three bugs: auto-off now uses an absolute deadline (`_autoOffAt`) so choosing a timer mid-session no longer expires immediately; the auto-off pills re-sync from state via `SyncTimerSelection` so they can't advertise a cancelled window; Pomodoro tracks ownership with `_pomHeldCaffeine` and releases only the keep-awake session it started. Corrected the `dotnet run` path in CONTRIBUTING.md. |
| 2026-07-29 | Added the Notes feature: a `Notes ↗` launcher segment in the tray window and a tray-menu entry open a new resizable 900×620 `WindowChrome` window with an Apple Notes-style list/editor split — pinned groups, search, right-click menu, debounced autosave, blank-note discard, themed delete confirmation, and persisted geometry/divider/selection. New `Notes/Note.cs` (model + pure derivation) and `Notes/NotesStore.cs` (atomic JSON I/O with corrupt-file quarantine) store notes in `%AppData%\Caffeine\notes.json` — a deliberate, approved departure from registry-only persistence, now scoped to settings. Added 7 amber theme keys to both dictionaries. Still zero NuGet packages. |
| 2026-07-29 | Brought Notes up to the tray window's finish: 12px rounded shell with a shadow gutter via `AllowsTransparency` alongside `WindowChrome` (corners and margin flatten when maximised), window entrance easing, editor cross-fade on note switch, selected-row slide-in, hover/press scale on every action button, per-`ScrollViewer` smooth wheel scrolling, a slim custom scrollbar, and the save error turned into a floating toast so it cannot square off the corners. Documented the shared motion vocabulary. |
| 2026-07-30 | Clicking a picture in a note now opens it in a viewer filling the notes surface — fit / zoom / pan, wheel and keyboard zoom, double-click to flip fit-vs-actual, dismiss by ✕, Escape or clicking the surround. Resolving the click needed a `TextPointer`: an editable `RichTextBox` swallows mouse input to embedded elements, and `InputHitTest` there returns text elements rather than visuals. A standalone full-screen window was tried first and dropped — its content laid out larger than the window and UI Automation could not see it at all. |
| 2026-07-30 | Gave Notes its own window ambience, as Pomodoro has: selecting it eases the whole window to a warm coffee (`NotesAmbient` — `#AD8A5F` light, `#54402A` dark), and `NotesWindow` adopts the same tint. Amber was deliberately not used for this: it is already the selection fill and a saturated amber field would swallow it. The editor pane became a floating card to match the list, so body text never sits on the tinted background. |
| 2026-07-30 | Moved the formatting bar out from between the title and the body: it now folds out **above** the title from an `Aa` toggle sitting beside the date line, is collapsed by default, and remembers its state. Tightened the editor's header margins in the process — with the bar collapsed the body starts about 50px higher than it did. |
| 2026-07-30 | Notes became a rich-text editor: the title is now its own field above the body rather than the body's first line, and the body is a `RichTextBox` with bold/italic/underline/strikethrough, a heading style, and bulleted/numbered lists (Tab nests a list item). Pictures can be attached, pasted or dragged in and are embedded in the note. Storage split in two as a result — `notes.json` is now an index carrying a plain-text mirror for search and previews, and each formatted body is its own `XamlPackage` file under `bodies\`, written only when that body changed. Notes from the plain-text era migrate on load. |
| 2026-07-30 | Refinements: dropped the handle from the mark so the cup is symmetric, and drew the toggle button from the cup body alone so `Stretch` centres it exactly in the circle. Made the tab strip's position conditional — it keeps its original centred row on Caffeine/Pomodoro/Settings and only moves into the title bar on Notes, where it is left-aligned to the same 14px margin as the floating notes card. Added **Recently Deleted**: delete now moves a note to a 30-day bin with a countdown per row, reachable from a footer in the list card, with Restore, a read-only editor, permanent deletion behind the confirmation dialog, and purge-on-load. The confirmation moved off the reversible delete and onto the irreversible one. |
| 2026-07-30 | New identity and a more uniform Notes: designed the app mark — a minimal stroked coffee cup that steams only when caffeine is on — and drove the tray icon, taskbar icon and the activate/deactivate button from the same vector paths, which also retired the HICON leak. Moved the Caffeine/Pomodoro/Notes strip into the title bar and dropped the window title (the checked tab says where you are), reclaiming a row of height. Rebuilt the notes list as a floating card on the tab strip's own fill and radius, and replaced per-row selection fills with a single indicator that travels between rows using the tab indicator's stretch-and-settle keyframes. |
| 2026-07-30 | Notes now transforms the tray window instead of always opening its own: extracted the whole notes UI into `Controls/NotesView` (a single instance `App` owns and reparents), made `Notes` a real tab beside Pomodoro, and added an eased window growth from 380×500 to 900×620 on selecting it. A pop-out button in the title bar moves the same view into `NotesWindow`, whose new dock button moves it back; the tab is disabled while popped out and re-enabled however that window is dismissed. `NotesWindow` shrank from 702+570 lines to 134+128 as a result. Inline Notes therefore inherits the real Caffeine title bar and segmented control, which is what it was missing. |
| 2026-07-30 | Added the **Todo** feature — a fifth panel beside Notes with Google Tasks' behaviour and the app's own surfaces. Multiple lists with colours and reordering, tasks with details, due date/time, one level of subtasks and five recurrence presets, a collapsible Completed section, three sort modes with drag-to-reorder, right-click Move to / Duplicate / Add subtask, delete with a six-second Undo, and tray-balloon reminders from a 30-second check in `App` so a task is announced whether or not the tab is open. New `Todo/` model, store (`tasks.json`) and registry-backed `TodoSettings`; new `Controls/TodoView` (shared and reparented, like Notes) and `TodoWindow`. Settings gained a TODO section (density, sort, default due time, Completed default), included in Reset to Defaults but never touching tasks. |
| 2026-07-30 | Released v2.0.0. Bumped `<Version>`, filled in the csproj package metadata, rewrote README.md around the four features rather than the one it started as, and added CHANGELOG.md and SECURITY.md. The security policy states the boundary plainly — no network, no elevation, HKCU only, two P/Invokes — because that boundary is the product promise. |
| 2026-07-30 | Retemplated the settings text inputs, the last place a stock Windows control showed through: the four fields (three Pomodoro custom durations and the Todo default due time) were drawing a white field with a system border on a dark card. `SettingsInputStyle` gives them the app's rounded surface, its type and caret colours, and an accent focus ring. The fill is `SurfaceHover` rather than `SurfaceColor`, which is the same value as `CardBackground` and would have made the field invisible against the card it sits on. |
| 2026-07-30 | Gave the mark its vapour. Redrew the two steam wisps symmetrically about the cup's centre with a wider, calmer curve, put a steaming cup on the Caffeine tab (where the mark names the feature rather than reporting state, and a bare cup read as a bucket), and made the toggle button steam while caffeine is on — each wisp drifting up and fading over 2.6 s, half a cycle apart, with the cup settling lower to make room. The toggle's mark moved onto a shared 24-unit canvas inside a `Viewbox` so the cup and the wisps stay in register instead of each stretching to its own box. |
| 2026-07-30 | Fixed the tab indicator sizing to the icon alone instead of icon-plus-label: the label is revealed by a template trigger on `IsChecked`, and WPF raises the `Checked` event *before* it applies that trigger, so measuring synchronously read the pre-reveal width. `PositionSegIndicator` now defers to `DispatcherPriority.Loaded` and measures once layout has actually run. |
| 2026-07-30 | Switched the tab strip from text to icons — cup, stopwatch, page, tick — with the label revealed only on the selected tab, so a fourth tab fits without widening the window. Added `TodoAmbient`/`TodoAccent`/`TodoSelectionFill`/`TodoOverdue` to both themes. Retemplated `Calendar`, `ContextMenu` and `MenuItem`, the first stock Windows-styled controls the app has used: cells needed `CalendarDayButtonStyle` rather than an implicit style, separators resolve `MenuItem.SeparatorStyleKey`, and `Data="{TemplateBinding Tag}"` silently yields no geometry. Fixed a crash from closing a `Popup` inside a mouse-up (WPF re-delivers the event into the tree it is tearing down), a picker that would not open for an already-dated task, and `Tab_Clicked` still mapping only three tabs so leaving Settings on Todo landed on Caffeine. Extracted `SmoothScroller` so both feature views share one copy. |
| 2026-08-05 | Added **prompt sets** to Notes: a `P` button beside `+` (and `Ctrl+Shift+P` — `Ctrl+Shift+N` is WPF's own ToggleNumbering and never reaches the window) creates a note whose body is a queue of prompts rather than a document. The title asks for an application name; the body holds `PROMPTS TO SEND` and `SENT` sections of numbered cards with a tick, copy and delete, drag-to-reorder by a grip, a `Sent today HH:mm` stamp, and a six-second Undo on delete. Built as a new `Note.Kind` rather than a sixth panel, which meant the list, search, pin, duplicate, Recently Deleted and blank-discard all worked unchanged — `PlainText` mirrors the prompt texts, so nothing downstream needed to know. New `Notes/Prompt.cs`; new `prompts\<id>.json` per note beside `bodies\`, with the same atomic write and quarantine rules plus a read-failure path that blocks writes rather than emptying a file it could not open. The list row gained a `P` badge and a `3/7 sent` count, both spelled into `AccessibleLabel` because the row template has no `ContentPresenter`. Still zero NuGet packages. |
| 2026-07-29 | Made Notes visually part of Caffeine rather than a Windows-styled window: replaced the rectangular caption buttons and red close hover with the tray window's 28px `SurfaceColor` circles (`─ □ ✕`, `❐` when maximised) including the 1.1 hover scale, matched the title bar's padding and 16px SemiBold title, turned the toolbar icons into the same circles, and aligned every radius, padding and type size to the tray window (see Shared control geometry). The editor's date line now uses `ScrollingTextBlock`, giving Notes the app's odometer motion. Fixed the `ScrollingTextBlock` accessible-text doubling as a prerequisite — that also corrects the tray window's five existing usages, which previously read "Active" as `AAccttiivvee`. |
