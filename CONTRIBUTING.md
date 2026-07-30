# Contributing to Caffeine

Thanks for your interest in contributing! Here's how to get started.

## Development Setup

1. Install [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. Clone the repo and open in your editor of choice
3. Build and run:

```bash
dotnet build
dotnet run
```

The project file lives at the repository root, so `dotnet run` needs no `--project` argument.

## Code Style

- File-scoped namespaces (`namespace Foo;`)
- Nullable reference types enabled
- No external dependencies — keep it lightweight
- Prefer WPF patterns: DependencyProperties, ResourceDictionaries, DynamicResource bindings

## Making Changes

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-change`)
3. Make your changes
4. Ensure `dotnet build` passes with no warnings
5. Test manually (this is a GUI app — run it and verify your changes work)
6. Commit with a clear message describing what and why
7. Open a Pull Request

## Reporting Bugs

Please include:
- Windows version (e.g., Windows 11 23H2)
- Steps to reproduce
- Expected vs actual behavior
- Screenshots if it's a visual issue

## Feature Requests

Open an issue describing:
- What you'd like to see
- Why it would be useful
- Any ideas on how it could work

## Project Structure

```
caffeine-win/
  App.xaml(.cs)           — Application entry, tray icon, keep-awake logic, the shared feature views
  MainWindow.xaml(.cs)    — Main UI window with tabs and settings
  NotesWindow.xaml(.cs)   — Optional standalone window for Notes
  TodoWindow.xaml(.cs)    — Optional standalone window for Todo
  ThemeManager.cs         — Light/dark theme detection and switching
  Notes/                  — Note model and the notes.json store
  Todo/                   — Task and list models, the tasks.json store, todo preferences
  Controls/               — NotesView, TodoView, ScrollingTextBlock, SmoothScroller
  Themes/                 — Light and dark ResourceDictionary files
```
