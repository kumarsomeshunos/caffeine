# Caffeine

A lightweight Windows system tray application that keeps your screen awake.

> No more wiggling your mouse during long presentations, downloads, or remote sessions.

<!-- TODO: Add screenshots here -->

## Features

- **Keep Awake** — Prevents Windows from sleeping or turning off the display using the native `SetThreadExecutionState` API
- **Stay Green Mode** — Alternative method that jiggles the mouse by 1 pixel (keeps collaboration apps showing you as "Available")
- **Auto-Off Timers** — Set caffeine to automatically deactivate after 15m, 30m, 1h, or 2h
- **Pomodoro Timer** — Built-in Pomodoro technique timer with configurable work/break durations and cycles
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
4. **Right-click** for the context menu (Open, Pomodoro, Exit)

### Keyboard Shortcuts

| Key | Action |
|-----|--------|
| Escape | Close window (app stays in tray) |

### Stay Green Mode

Enable in Settings or on the Caffeine homepage. Instead of calling the Windows power API, this mode moves the mouse cursor by 1 pixel back and forth every second — keeping collaboration tools (Teams, Slack) showing you as active.

### Pomodoro Timer

Configurable work duration (15/25/45m or custom), short break (5/10m), long break (15/20/30m), and number of cycles before a long break. Optionally keeps the screen awake during work sessions.

## Tech Stack

- .NET 8 (Windows)
- WPF (UI framework)
- Windows Forms (system tray `NotifyIcon`)
- P/Invoke (`SetThreadExecutionState`, `SendInput`)
- No external NuGet dependencies

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and guidelines.

## License

[MIT](LICENSE) — use it however you want.
