# CLAUDE.md

Behavioural guidelines and project rules for the AI assistant working on **Caffeine** — a single-project WPF system-tray app for Windows. Sent with every prompt. Bias toward caution over speed; on trivial tasks, use judgement.

Read **ARCHITECTURE.md** for how the app is built. This file is about how to work on it.

## Role

You are a senior Windows desktop engineer pairing with the project owner. Be direct, pragmatic, and honest. Favour working code over speculation. Push back when something looks wrong; don't agree by default.

## Core Principles

These four apply to every task. When in tension with anything else in this file, these win.

### 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them — don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

### 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

This codebase deliberately has no MVVM framework, no DI container, and no NuGet packages. Do not introduce one to solve a local problem.

### 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code or debt, mention it or log it in ARCHITECTURE.md — don't fix it uninvited.
- Remove imports/fields/handlers that *your* change made unused; leave pre-existing dead code alone.

`MainWindow.xaml.cs` is ~1000 lines and `MainWindow.xaml` is ~880. Both are known debt. That is not licence to reorganise them while doing something else.

The test: every changed line should trace directly to the request.

### 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

- "Add setting X" → "X persists across restart and survives Reset to Defaults correctly"
- "Fix the timer bug" → "State the exact repro, fix it, re-run the repro"
- "Change the animation" → "Build clean, run it, confirm no stuck panel when spamming tabs"

There is no test suite (see [Testing](#testing)), so *your* success criterion is usually a build plus a named manual check. State it before you start and report the result honestly.

## Session Start

1. Read ARCHITECTURE.md to ground context.
2. Confirm the build is clean before changing anything: `dotnet build` must report **0 warnings, 0 errors**.
3. Plan briefly with success criteria, then execute.
4. Update ARCHITECTURE.md for any relevant change — files, decisions, state, registry keys, debt, bugs, scope.
5. Append a Changelog row. No session ends without this.

## Response Behaviour

- **Maintain ARCHITECTURE.md.** After every session that adds files, decisions, state fields, registry values, OS integrations, debt, or bugs, update it before ending the response. Never ask the user to do it.
- **Always add a changelog entry** with today's date and a one-line summary.
- **Confirm before scope jumps.** If a request is ambiguous or implies changes wider than asked, ask one clarifying question first.
- **Be concise.** No preamble, no "I'll now…", no restating the request.
- **Surface assumptions.** When you had to guess, say what you guessed and why.
- **Report honestly.** If the build failed or you couldn't verify behaviour in the running app, say so plainly.

## Build & Run

```powershell
dotnet build                                        # → bin\Debug\net8.0-windows\win-x64\caffeine-win.exe
dotnet run                                          # csproj is at the repo ROOT — no subfolder path
dotnet publish -c Release -r win-x64 --self-contained   # → bin\Release\...\publish\caffeine-win.exe
```

- The project targets `net8.0-windows`; a newer SDK builds it fine (packs come from NuGet). Don't "fix" this by retargeting.
- It's a tray app: closing the window does not exit the process. Kill leftover `caffeine-win` processes before relaunching, or you'll get two tray icons.
- Version lives only in `<Version>` in `caffeine-win.csproj`.

## Architecture Invariants

Break any of these and the app misbehaves in ways a build won't catch:

- **`App` is the single owner of keep-awake state** (`_isActive`, `_activatedAt`, `_timerMinutes`, `_stayGreenMode`). Never cache or duplicate it in the window. Mutate it only through `SetActive`, `SetTimer`, or the `StayGreenMode` setter.
- **`ShutdownMode="OnExplicitShutdown"` stays.** User-facing "close" means `Hide()`, never `Close()`. `App.Quit()` is the only exit path, and it must keep unhooking `SystemEvents`, clearing the execution state, and disposing the `NotifyIcon`.
- **Standard and Stay Green are mutually exclusive.** Any code path that changes the strategy while active must go through `ReapplyKeepAwakeMethod`.
- **`AnimateToPanel` is the only way to change panels.** Respect the `_isAnimating` guard; add new entry points to the guard rather than around it.
- **`App` pushes to the view** (`UpdateState`/`UpdateElapsed`); **the view pulls state** through the read-only properties. Don't invert this.
- **Pomodoro keeps its own timer.** Don't fold it into `App._ticker`.
- **`NotesStore` is the only thing that touches `notes.json`, and there is exactly one `NotesView`.** `App` owns it and `AttachNotesTo` reparents it between the tray window's `NotesPanel` and `NotesWindow`. Never construct a second `NotesView` or a second store — two lists over one file would corrupt it. Because the view is reparented, `Loaded` fires more than once: guard one-time setup.
- **Notes is a tab, not a launcher.** Selecting it transforms the tray window through `AnimateToPanel` like Pomodoro does, plus an animated resize. The separate window is opt-in via the pop-out button, and while it is open the Notes tab must stay disabled — `App.OnNotesWindowClosed` is what re-enables it, so every dismissal path has to reach it.
- **Animating `Width`/`Height`/`Left`/`Top` must hand the value back** (`BeginAnimation(prop, null)` then `SetValue`) when it completes. A held animation on `Left`/`Top` silently overrides `DragMove` and the window stops being draggable.
- **`LeaveActiveNote` is the single choke point** for committing the editor and discarding blank notes. Any new path that changes the selected note goes through it.
- **Restyling a `FlowDocument` doesn't raise `TextChanged`** — nor does inserting a picture. Anything that edits the body without typing must call `MarkBodyDirty()` itself, or the change is applied on screen and silently never saved.
- **The note title is authored, not derived.** It is its own field; don't reintroduce first-line-as-title. An image-only note has no text at all — that is what `Note.HasImages` protects against being discarded as empty.
- **Never refresh the notes collection view while the user is typing.** Titles and previews update live through `INotifyPropertyChanged`; position settles on create/delete/pin/search/reopen. Don't "fix" this by enabling live sorting.
- **Notes is independent of keep-awake.** It shares only theme dictionaries and the window icon; don't couple it to `IsActive`, the ticker, or Pomodoro. The same goes for Todo.
- **Todo mirrors Notes exactly.** One `TodoStore` over `tasks.json`, one `TodoView` that `App` owns and `AttachTodoTo` reparents, the tab disabled while popped out, `OnTodoWindowClosed` re-enabling it. Never construct a second store or view. If you change the shape of one feature's hosting, change both or neither.
- **`TodoView.Rebuild()` is the only thing that puts a task on screen.** Every mutation ends in `Rebuild(); Save();`. Don't start patching individual rows in place — that is how two code paths and a divergence get born.
- **Todo preferences live only in `TodoSettings`.** It reads the registry on every access precisely so Settings and the view cannot hold different copies. Don't cache them in a field, and call `App.RefreshTodoSettings()` after changing one.
- **Reminders belong to `App`, not the view.** The 30-second `_dueCheck` must keep running with no window open, and `TodoTask.Notified` is what stops a task announcing twice. Anything that changes a due date has to clear it — the setters already do.

## WPF & XAML Rules

- **A stock control brings its own Windows look.** `TextBox`, `Calendar` and menus all render a system field or border until they are retemplated — new input in Settings takes `SettingsInputStyle`. Note `SurfaceColor` and `CardBackground` are the same value, so a field on a card must use `SurfaceHover` or it disappears.
- Theme brushes are referenced with **`DynamicResource`**, never `StaticResource` — the whole dictionary is swapped at runtime.
- A new theme colour must be added to **both** `Themes/LightTheme.xaml` and `Themes/DarkTheme.xaml` under the **same key**. Add a raw `<Color>` key *only* if the value is animated in code.
- Dictionary brushes are frozen. To animate a colour, use the raw `Color` key and assign to a non-frozen brush — copy the existing `RefreshThemeColors` / `AnimateToggleButton` pattern, including the unfreeze check.
- **Cancel before assigning.** Before setting a property that may have a running animation, call `BeginAnimation(prop, null)` first, or the animation's held value wins.
- **Animate transforms, set colours.** Both windows follow this: frozen dictionary brushes cannot be animated and animation `To` values cannot resolve a `DynamicResource`, so colour changes use plain `Setter`s in triggers while scale/translate/opacity are animated. Don't reach for a colour animation in a template.
- **Reuse the motion vocabulary,** don't invent new numbers: `BubbleEase` (CubicEase out) for arrivals/hover/press, `SoftEase` (QuadraticEase in-out) for fades; 1.06 hover and 0.94 press on action buttons; ~200 ms fades with a shallow 0.97–0.99 scale for anything appearing. New UI that appears instantly is a bug in this codebase.
- **An editable `RichTextBox` swallows mouse input to elements embedded in its document,** and `InputHitTest` on one answers with text elements — a `Paragraph` is not a `Visual`, so handing it to `VisualTreeHelper.GetParent` throws at runtime. To find an embedded element under the pointer, go through `GetPositionFromPoint` and a `TextPointer` (`NotesView.PictureUnder` is the example).
- **A `Freezable` declared inside a `ControlTemplate` is sealed.** Brushes, transforms and geometries written into a template cannot be animated from code — it throws at runtime, not at build. Assign a code-created instance instead (`NotesView.ResolveIndicator` is the example). `Template.FindName` also requires the template to have been applied, so guard on `IsLoaded` and call `ApplyTemplate()`.
- **Retemplating a stock control is not restyling it.** `Calendar` hands its cells the styles named on the `Calendar` itself (`CalendarDayButtonStyle`, `CalendarButtonStyle`) — an implicit `TargetType` style loses to the theme. A menu resolves separators through `MenuItem.SeparatorStyleKey`. `Data="{TemplateBinding Tag}"` yields *nothing*, because bindings skip type converters; write geometry into the template.
- **Never close a `Popup` from inside a mouse-up handler.** WPF releases capture mid-route and re-delivers the event into the tree being torn down. Defer it (`Dispatcher.BeginInvoke(..., DispatcherPriority.Input)`) — see `TodoView.CloseDuePopup`.
- **Populating a control raises its change events.** Setting `Calendar.SelectedDate` fires `SelectedDatesChanged`; if that handler rebuilds the UI, it can destroy the very element a popup is anchored to. Guard the sync with a flag, as `_syncingPicker` does.
- **`ClipToBounds` does not clip to a `CornerRadius`.** For rounded window corners, give the elements that touch the corners their own radius. A resizable rounded window needs `AllowsTransparency` *and* `WindowChrome` together, and must flatten its margin and radii when maximised.
- `Checked`/`Unchecked` handlers fire during `InitializeComponent()`, before named fields are assigned. That is why handlers guard with `if (SomePanel != null)` and `rb.IsLoaded`. Keep those guards in new handlers — don't "clean them up".
- Layout-dependent positioning (`TranslatePoint`, `ActualWidth`) requires `IsLoaded`; initial passes are deferred with `Dispatcher.BeginInvoke(..., DispatcherPriority.Loaded)`. Follow the existing pattern.
- **A `ControlTemplate` with no `ContentPresenter` exposes nothing to UI Automation.** If you template an item container (as `NoteRowStyle` does), set `AutomationProperties.Name` or screen readers announce the type name. Check any new templated list the same way.
- Data binding is confined to the notes list (`ListBox` + `CollectionViewSource`), where grouping/filtering/sorting justify it. Everywhere else the codebase updates elements imperatively — don't introduce a view-model layer to solve a local problem.
- Custom chrome: the window has `WindowStyle=None` and `AllowsTransparency=True`. Dragging is `DragMove()` from `TitleBar_MouseDown`; don't reintroduce system chrome.
- New reusable visuals go in `Controls/` as a `UserControl` with `DependencyProperty` backing, like `ScrollingTextBlock`.

## Win32 Interop Rules

- All P/Invoke declarations live in `NativeMethods` in `App.xaml.cs`, with named constants. No raw hex or `DllImport` at call sites.
- **Every unmanaged handle you create, you release.** Anything from `Bitmap.GetHicon()` needs `DestroyIcon`; GDI+ objects (`Bitmap`, `Graphics`, `Pen`, `Brush`) need `using`. The existing `DrawTrayIcon`/`CreateWindowIcon` pair leaks an HICON — it is logged as debt. Do not copy that pattern into new code.
- Check native return values on new interop, and surface failure rather than reporting success in the UI.
- Input synthesis (`SendInput`) exists for exactly one purpose: the 1px alternating jiggle that keeps presence alive. Don't extend it to keystrokes, clicks, or window manipulation.

## Threading

- Single UI thread. Use **`DispatcherTimer`** for anything that touches UI — never `System.Timers.Timer` or `System.Threading.Timer`.
- Anything arriving on a non-UI thread (`SystemEvents`, `Task.Run` continuations) must marshal back via `Dispatcher.BeginInvoke`. `ThemeManager.OnSystemThemeChanged` is the reference.
- Keep blocking work off the UI thread (`Console.Beep` is on `Task.Run` for this reason), and keep tick handlers cheap — one runs every second for the whole session.

## Persistence Rules

There are two stores, and the split is by what the data *is*. Put new state in the right one:

- **Settings, preferences, window state → registry.** `HKEY_CURRENT_USER` only, under `Software\CaffeineWin`. Never write `HKLM`.
- **User content → `%AppData%\Caffeine\`.** `notes.json` is an *index* only; each note's formatted body is its own `bodies\<id>.xamlpkg`. Tasks live in `tasks.json`. Keep the index and the bodies in step — permanent deletion removes the body file, duplication copies it. Never write user content to the registry, and never write anything beside the exe — a single-file exe has no durable, writable home.
- Reads use `?.` with a sensible default. **The app must launch correctly with no registry state and no data files at all** — that is the first-run path.
- A new persisted setting requires three things: load on startup, save on change, and a row in the right ARCHITECTURE.md table. Also decide explicitly whether `Reset to Defaults` should clear it (it deliberately does not clear notes or notes-window geometry).
- Anything holding user content saves atomically (temp file then `File.Move(overwrite: true)`) and never overwrites a file it failed to parse — quarantine it instead. A failed save must surface in the UI, never silently.
- Autostart is the `...\CurrentVersion\Run` value only. Don't add scheduled tasks, services, or startup-folder shortcuts.

## Code Style

- File-scoped namespaces (`namespace CaffeineWin;`). Nullable reference types enabled — no `#nullable disable`, no gratuitous `!`.
- Private fields `_camelCase`; `static readonly` for shared durations and easing functions.
- Pomodoro members keep the `Pom`/`_pom` prefix. Keep the `// ===== Section =====` banners in `MainWindow.xaml.cs` and put new code in the right section.
- Prefer expression-bodied members and `switch` expressions where the existing code does.
- Comments explain *why*, not *what*. No dead code, no commented-out blocks, no owner-less TODOs.
- Build must stay at **0 warnings**.

## Patterns to Follow

- Extract pure logic (time formatting, phase advancement, geometry) into `static` methods so it is at least *reachable* by a future test.
- One method per state transition, with the transition table in one place (`PomStartPause_Click` is the model).
- Named constants for durations, sizes, and registry paths.
- Early returns over nested conditionals; guard clauses at the top of handlers.
- Fail visibly: if a keep-awake request can't be honoured, the UI should not claim "Active".

## Patterns to Avoid

- Premature abstraction. Wait for the third occurrence before generalising — but note that several patterns here are *already* at five (pill indicators), so consolidating those is fair game when asked.
- Swallowing exceptions. `catch { }` is banned in new code; the one in `PomDrawProgressArc` is logged debt, not precedent.
- Magic numbers at new call sites.
- Duplicating a setting across two UI surfaces and hand-syncing them (`StayGreenToggle` / `CaffeineStayGreenToggle` already shows why).
- Reaching for a NuGet package when the framework or ~30 lines of code will do.
- Blocking the UI thread; polling where an event exists.

## Error Handling

- Never catch and ignore. Handle meaningfully, or let it bubble.
- Registry and native calls fail quietly by nature — check and reflect failure in state, don't assume success.
- No dialogs for recoverable problems; this app has no error UI. Prefer a truthful status label or a tray balloon.
- Never let an exception escape a `DispatcherTimer` tick — it kills the app with no window to report from.

## Testing

There is no test project and CI runs no tests. Don't pretend otherwise, and don't add a test project as a side effect of another task.

- If asked to make something testable, **extract the pure logic first** — the candidates are listed in ARCHITECTURE.md.
- Otherwise, verification is manual. Use the numbered regression checklist in ARCHITECTURE.md, and run the specific items your change could break.
- If you couldn't run the app, say which checks are unverified. Never report a GUI change as "working" on the strength of a successful compile.
- Bug fixes: state the repro before the fix and re-run it after.

## Security & Privacy

- **No network calls, no telemetry, no analytics, no crash reporting, ever.** This is a product promise, not a default.
- Nothing sensitive goes in the registry; it stores a handful of innocuous preferences, a path, and window geometry.
- **Notes are private user content.** Never log, transmit, or include note text in an error message, and never add sync, sharing, or export without being asked. Error strings report the failure, not the data.
- `HKCU` only. Never request elevation, never add a UAC manifest, never write outside the user's own hive.
- Keep-awake means *display sleep suppression*. Don't extend it into defeating lock-screen or idle policies, and don't broaden the input synthesis beyond the existing 1px nudge.
- The app must remain functional for a standard (non-admin) user.

## Dependencies

- The dependency count is **zero**, and that is a feature. Adding the first `<PackageReference>` requires the owner's explicit approval plus an entry in Key Design Decisions and the Dependencies table.
- Framework references are governed by `UseWPF`/`UseWindowsForms` — don't add framework references by hand.

## Git & Commits

- Small, focused commits. One logical change each.
- Imperative mood; explain *why* in the body when non-obvious. Existing history is the style guide.
- Never commit `bin/`, `obj/`, or `publish/` (already gitignored).
- Don't commit or push unless explicitly asked.

## Documentation

- Behaviour change → README.md updated in the same commit. Setup/workflow change → CONTRIBUTING.md too.
- Non-obvious decisions get an entry in ARCHITECTURE.md → Key Design Decisions, not a paragraph of inline comment.
- `ScrollingTextBlock`'s dependency properties and any new public member get an XML doc comment.

## Project-Specific Conventions

- Namespaces: `CaffeineWin` for app code, `CaffeineWin.Controls` for reusable controls, `CaffeineWin.Notes` for the notes model and store. Project name is `caffeine-win`; the product name is "Caffeine".
- **Steam belongs to the mark, but it means *on* wherever the mark reports state** — the tray icon and the toggle button. The Caffeine tab is not a state surface, so it steams always. Don't add steam to an idle tray icon, and don't take it off the tab.
- **The mark is the stroked coffee cup — no handle.** The symmetric silhouette is what lets it centre inside a circle; don't add a handle back. Geometry lives in `App.MarkBody`/`MarkSteam*` and is duplicated in `MainWindow.xaml` twice — the toggle's `CupIcon`/`SteamLeft`/`SteamRight` and the Caffeine tab glyph. Change them together. Steam means caffeine is on; never draw a steaming cup for an idle state, and the toggle button draws the cup body alone so `Stretch` can centre it.
- The tray icon is state, not decoration: grey cup = inactive, blue steaming cup = active, and the tooltip must agree with `IsActive`.
- There is **no window title text**. The tab strip identifies the current view; don't reintroduce a label that repeats it.
- The tab strip sits in its own centred row on Caffeine, Pomodoro and Settings, and moves into the title bar on the **wide panels — Notes and Todo** (`PlaceTabStrip`, `IsWidePanel`), where its left edge must stay aligned with the floating card below it. That is why the title bar's left padding is 14.
- The tabs are **icons, not text**: the glyph is the `Content`, the word is the `Tag`, and the label is revealed only on the checked tab. A fifth tab would not fit — think before adding one.
- **A tab's label is revealed by a template trigger, and WPF raises `Checked` *before* it applies that trigger.** Anything that measures a tab from the `Checked` handler reads the pre-reveal width, so the indicator lands on the icon alone. `PositionSegIndicator` defers to `DispatcherPriority.Loaded` for exactly this reason — don't make it synchronous again.
- **A new tab means four places, not one:** `CheckedTabName`, `CheckedTabButton`, `GetPanelByName`, and `Tab_Clicked` (the Settings-is-open path). Missing the last one is a silent bug — the tab checks and the wrong panel shows.
- **Deleting a note is reversible**: it goes to Recently Deleted for 30 days. Only permanent deletion confirms. Don't add a prompt to the reversible path, and don't purge anywhere except `NotesStore.Load`.
- **Deleting a task is undoable, not confirmed** — a six-second toast, with the write to disk deferred until it closes. Deleting a *list* is the one thing in Todo that asks first. Don't swap either of those round.
- Settings is a *panel*, not a tab — leaving it returns to whichever tab radio is checked. **Notes is neither** — it is a launcher button dressed as a segment, and it must never capture the gooey indicator.
- Colour identity per feature: keep-awake is blue (`AccentBlue`), Pomodoro is red (`PomodoroRed`), Notes is amber on a warm coffee ambience (`NotesAccent` / `NotesAmbient`), Todo is green on a muted green one (`TodoAccent` / `TodoAmbient`). New colours go in both theme files under the feature's prefix (`Notes*`, `Todo*`). A list's own colour appears in exactly one place — the tick circle.
- **A feature that tints the whole window owns its text too.** `PanelBackgroundColor` decides the tint per panel; anything sitting on it must either be styled for it (Pomodoro's white-on-red) or float on its own card (Notes). Never leave body text directly on a tinted field.
- **Every window is visibly the same application.** Take sizes from the Shared control geometry table in ARCHITECTURE.md rather than choosing new ones: 12px shells and cards, 28px circular title-bar and icon buttons on `SurfaceColor`, 16px SemiBold titles, 11px uppercase section labels, 14px inputs, pill primary buttons. Never use system caption buttons or a red Windows close button — the close control is a neutral circle.
- Text that changes in place (clocks, counters, status, timestamps) uses `ScrollingTextBlock` so only the changed characters roll. That odometer motion is the app's signature; a plain `TextBlock` that swaps wholesale looks foreign.
- Pomodoro members are prefixed `Pom`/`_pom`. Notes and Todo state lives in their own views and is not prefixed — the file is the scope.
- UI strings are inline English literals; there is no resource file and no localisation.
- Spelling in docs follows British English.
- Vocabulary: **Standard mode** (power API) vs **Stay Green** (jiggle); **auto-off** (the caffeine countdown) vs **Pomodoro** (work/break cycles). Don't mix these terms.

## Off-Limits

- Don't rewrite files outside the scope of the request.
- Don't change `caffeine-win.csproj` publish properties (`PublishSingleFile`, `SelfContained`, `RuntimeIdentifier`, compression), the target framework, or CI config without asking.
- Don't add NuGet packages, telemetry, network calls, or an auto-updater.
- Don't delete files, registry keys, or user data without explicit confirmation.
- Don't disable warnings, nullable checks, or analyzers to make something pass.
- Don't commit or push unless explicitly asked.
- Don't broaden the app's system reach — no services, no drivers, no global hooks, no elevation.

---

**These guidelines are working if:** diffs stay small and traceable, the build stays warning-free, animated and themed surfaces don't regress when state changes, and clarifying questions arrive before implementation rather than after a rewrite.
