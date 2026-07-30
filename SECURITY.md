# Security Policy

## Supported versions

Only the latest release receives fixes. Please reproduce a problem on the newest version before
reporting it.

| Version | Supported |
| ------- | --------- |
| 2.0.x   | Yes       |
| < 2.0   | No        |

## Reporting a vulnerability

Please **do not open a public issue** for a security problem.

Use GitHub's [private vulnerability reporting](https://github.com/kumarsomeshunos/caffeine/security/advisories/new)
instead. Include the version, your Windows build, what you did, and what happened. A proof of concept
helps but is not required.

This is a small project maintained by one person in their own time — expect an acknowledgement within
about a week, and please give a reasonable window for a fix before disclosing publicly.

## What Caffeine does with your machine

Worth stating plainly, because it bounds what a vulnerability here could reach:

- **No network access.** The app makes no HTTP requests, opens no sockets, and has no telemetry,
  analytics, crash reporting or update check. It ships with zero third-party packages, so there is no
  transitive dependency doing it on the app's behalf either.
- **No elevation.** It never requests administrator rights and works as a standard user. It reads and
  writes `HKEY_CURRENT_USER` only, never `HKEY_LOCAL_MACHINE`.
- **User content stays in your profile.** Notes and tasks live in `%AppData%\Caffeine`. Nothing is
  written beside the executable or anywhere outside your own profile.
- **Autostart** is a single value under `...\CurrentVersion\Run`, added only when you turn the setting
  on and deleted when you turn it off. No scheduled tasks, services or startup-folder shortcuts.
- **Two P/Invokes.** `SetThreadExecutionState` asks Windows not to blank the display, and `SendInput`
  moves the cursor one pixel in Stay Green mode. Input synthesis is used for that and nothing else —
  no keystrokes, no clicks, no window manipulation.
- **Display sleep only.** Caffeine suppresses display and idle sleep. It does not defeat the lock
  screen and is not a way around a policy your administrator has set.

## Note on unsigned binaries

Released executables are **not code-signed**, so Windows SmartScreen will warn on first run. If that
is not acceptable in your environment, build from source — the repository is the whole story, and
`dotnet publish -c Release -r win-x64 --self-contained` reproduces the released binary.
