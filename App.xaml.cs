using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Imaging = System.Windows.Media.Imaging;
using Media = System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace CaffeineWin;

internal static class NativeMethods
{
    [DllImport("kernel32.dll")]
    public static extern uint SetThreadExecutionState(uint esFlags);

    public const uint ES_CONTINUOUS = 0x80000000;
    public const uint ES_DISPLAY_REQUIRED = 0x00000002;

    [DllImport("user32.dll")]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public const uint INPUT_MOUSE = 0;
    public const uint MOUSEEVENTF_MOVE = 0x0001;
}

public partial class App : Application
{
    private const string SettingsPath = @"Software\CaffeineWin";

    private WinForms.NotifyIcon _trayIcon = null!;
    private MainWindow? _mainWindow;
    private NotesWindow? _notesWindow;
    private Controls.NotesView? _notesView;
    private TodoWindow? _todoWindow;
    private Controls.TodoView? _todoView;

    /// <summary>
    /// Tasks are loaded at startup rather than when the Todo tab is first opened: a due reminder has
    /// to fire whether or not anyone has looked at the list this session.
    /// </summary>
    public Todo.TodoStore TodoStore { get; } = new();

    private System.Windows.Threading.DispatcherTimer _dueCheck = null!;
    private bool _isActive;
    private DateTime _activatedAt;
    private int _timerMinutes;
    private DateTime _autoOffAt;
    private System.Windows.Threading.DispatcherTimer _ticker = null!;
    private bool _stayGreenMode;
    private bool _jiggleForward = true;

    public bool IsActive => _isActive;
    public DateTime ActivatedAt => _activatedAt;
    public int TimerMinutes => _timerMinutes;

    public TimeSpan AutoOffRemaining =>
        _isActive && _timerMinutes > 0 ? _autoOffAt - DateTime.Now : TimeSpan.Zero;

    public bool StayGreenMode
    {
        get => _stayGreenMode;
        set
        {
            _stayGreenMode = value;
            SaveStayGreenPreference(value);
            if (_isActive) ReapplyKeepAwakeMethod();
            _mainWindow?.UpdateState();
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ThemeManager.Initialize();
        LoadStayGreenPreference();

        _ticker = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _ticker.Tick += OnTick;

        TodoStore.Load();

        // Slow and always running: a task falling due must be announced even if the Todo tab has
        // never been opened, and a minute's granularity is plenty for a reminder.
        _dueCheck = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _dueCheck.Tick += (_, _) => CheckDueTasks();
        _dueCheck.Start();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open", null, (s, ev) => ShowMainWindow());
        menu.Items.Add("Pomodoro", null, (s, ev) => ShowMainWindow("pomodoro"));
        menu.Items.Add("Notes", null, (s, ev) => ShowNotes());
        menu.Items.Add("Todo", null, (s, ev) => ShowTodo());
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (s, ev) => Quit());

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = DrawTrayIcon(active: false),
            Text = "Caffeine — Inactive",
            Visible = true,
            ContextMenuStrip = menu
        };

        _trayIcon.MouseClick += OnTrayClick;
        _trayIcon.MouseDoubleClick += OnTrayDoubleClick;

        ShowMainWindow();
    }

    private void OnTrayClick(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button == WinForms.MouseButtons.Left)
            ToggleActive();
    }

    private void OnTrayDoubleClick(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button == WinForms.MouseButtons.Left)
            ShowMainWindow();
    }

    public void ToggleActive()
    {
        SetActive(!_isActive);
    }

    public void SetActive(bool active)
    {
        _isActive = active;

        if (_isActive)
        {
            if (!_stayGreenMode)
            {
                NativeMethods.SetThreadExecutionState(
                    NativeMethods.ES_CONTINUOUS | NativeMethods.ES_DISPLAY_REQUIRED);
            }
            _activatedAt = DateTime.Now;
            if (_timerMinutes > 0)
                _autoOffAt = _activatedAt.AddMinutes(_timerMinutes);
            SetTrayIcon(active: true);
            _trayIcon.Text = "Caffeine — Active";
            _ticker.Start();
        }
        else
        {
            NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS);
            SetTrayIcon(active: false);
            _trayIcon.Text = "Caffeine — Inactive";
            _ticker.Stop();
            _timerMinutes = 0;
        }

        _mainWindow?.UpdateState();
    }

    public void SetTimer(int minutes)
    {
        _timerMinutes = minutes;

        if (minutes > 0)
        {
            // Count down from when the timer was chosen, not from activation — otherwise
            // picking 15m during an already-longer session expires it on the next tick.
            if (_isActive)
                _autoOffAt = DateTime.Now.AddMinutes(minutes);
            else
                SetActive(true);
        }

        _mainWindow?.UpdateState();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_isActive && _timerMinutes > 0 && DateTime.Now >= _autoOffAt)
        {
            SetActive(false);
            return;
        }

        if (_isActive && _stayGreenMode)
            JiggleMouse();

        _mainWindow?.UpdateElapsed();
    }

    private void JiggleMouse()
    {
        var input = new NativeMethods.INPUT[1];
        input[0].type = NativeMethods.INPUT_MOUSE;
        input[0].mi.dx = _jiggleForward ? 1 : -1;
        input[0].mi.dy = 0;
        input[0].mi.dwFlags = NativeMethods.MOUSEEVENTF_MOVE;
        NativeMethods.SendInput(1, input, Marshal.SizeOf<NativeMethods.INPUT>());
        _jiggleForward = !_jiggleForward;
    }

    private void ReapplyKeepAwakeMethod()
    {
        if (_stayGreenMode)
        {
            NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS);
        }
        else
        {
            NativeMethods.SetThreadExecutionState(
                NativeMethods.ES_CONTINUOUS | NativeMethods.ES_DISPLAY_REQUIRED);
        }
    }

    private void LoadStayGreenPreference()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsPath, false);
        _stayGreenMode = key?.GetValue("StayGreenMode") is int v && v == 1;
    }

    private void SaveStayGreenPreference(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsPath);
        key.SetValue("StayGreenMode", enabled ? 1 : 0, RegistryValueKind.DWord);
    }

    public void ShowMainWindow(string tab = "caffeine")
    {
        if (_mainWindow == null || !_mainWindow.IsLoaded)
        {
            _mainWindow = new MainWindow();
            _mainWindow.ShowTab(tab);
            _mainWindow.Show();
        }
        else
        {
            _mainWindow.ShowTab(tab);
            _mainWindow.Show();
            _mainWindow.Activate();
        }
    }

    /// <summary>
    /// The single notes view. It is reparented between the tray window's Notes panel and
    /// <see cref="NotesWindow"/>, so there is only ever one store open over notes.json.
    /// </summary>
    public Controls.NotesView NotesView => _notesView ??= new Controls.NotesView();

    /// <summary>The single todo view, reparented between the tray window's Todo panel and its window.</summary>
    public Controls.TodoView TodoView => _todoView ??= new Controls.TodoView(TodoStore);

    public bool TodoPoppedOut => _todoWindow is { IsLoaded: true };

    public void AttachTodoTo(Panel host)
    {
        var view = TodoView;

        if (view.Parent is Panel previous)
        {
            if (ReferenceEquals(previous, host)) return;
            previous.Children.Remove(view);
        }

        host.Children.Add(view);
    }

    public void ShowTodo()
    {
        if (TodoPoppedOut)
        {
            RaiseTodoWindow();
            return;
        }

        ShowMainWindow("todo");
    }

    public void PopOutTodo()
    {
        if (TodoPoppedOut)
        {
            RaiseTodoWindow();
            return;
        }

        _mainWindow?.ShowTab("caffeine");

        _todoWindow = new TodoWindow();
        _todoWindow.Show();
        _mainWindow?.SetTodoPoppedOut(true);
    }

    public void DockTodo()
    {
        var window = _todoWindow;
        _todoWindow = null;

        _mainWindow?.SetTodoPoppedOut(false);
        ShowMainWindow("todo");

        window?.Close();
    }

    /// <summary>Settings changed a todo preference; rebuild the list if it has ever been built.</summary>
    public void RefreshTodoSettings() => _todoView?.RefreshSettings();

    /// <summary>Called however the todo window goes away, so its tab is never left disabled.</summary>
    public void OnTodoWindowClosed()
    {
        _todoWindow = null;
        _mainWindow?.SetTodoPoppedOut(false);
    }

    private void RaiseTodoWindow()
    {
        if (_todoWindow == null) return;

        if (_todoWindow.WindowState == WindowState.Minimized)
            _todoWindow.WindowState = WindowState.Normal;

        _todoWindow.Show();
        _todoWindow.Activate();
    }

    /// <summary>
    /// Announces tasks that have come due. Runs on its own slow timer rather than the keep-awake
    /// ticker, which only runs while caffeine is active.
    /// </summary>
    private void CheckDueTasks()
    {
        var due = TodoStore.DueForReminder(DateTime.Now).ToList();
        if (due.Count == 0) return;

        foreach (var task in due)
            task.Notified = true;

        ShowBalloon(
            due.Count == 1 ? "Task due" : $"{due.Count} tasks due",
            due.Count == 1 ? due[0].DisplayTitle : string.Join(", ", due.Take(3).Select(t => t.DisplayTitle)));

        TodoStore.Save();
    }

    /// <summary>True while Notes is living in its own window rather than in the tray window.</summary>
    public bool NotesPoppedOut => _notesWindow is { IsLoaded: true };

    /// <summary>Moves the shared view into a host, detaching it from wherever it was.</summary>
    public void AttachNotesTo(Panel host)
    {
        var view = NotesView;

        if (view.Parent is Panel previous)
        {
            if (ReferenceEquals(previous, host)) return;
            previous.Children.Remove(view);
        }

        host.Children.Add(view);
    }

    /// <summary>Opens Notes wherever it currently belongs — tray panel, or its own window.</summary>
    public void ShowNotes()
    {
        if (NotesPoppedOut)
        {
            RaiseNotesWindow();
            return;
        }

        ShowMainWindow("notes");
    }

    public void PopOutNotes()
    {
        if (NotesPoppedOut)
        {
            RaiseNotesWindow();
            return;
        }

        // Leave the tray window on Caffeine first so it shrinks back before the view moves out.
        _mainWindow?.ShowTab("caffeine");

        _notesWindow = new NotesWindow();
        _notesWindow.Show();
        _mainWindow?.SetNotesPoppedOut(true);
    }

    public void DockNotes()
    {
        var window = _notesWindow;
        _notesWindow = null;

        _mainWindow?.SetNotesPoppedOut(false);
        ShowMainWindow("notes");

        // Closing after the re-attach means OnClosing still flushes, but into an empty host.
        window?.Close();
    }

    /// <summary>
    /// Called however the notes window goes away — docked, closed with ✕, or Escape. Without this
    /// the tray window's Notes tab would stay disabled with nowhere to dock back from.
    /// </summary>
    public void OnNotesWindowClosed()
    {
        _notesWindow = null;
        _mainWindow?.SetNotesPoppedOut(false);
    }

    private void RaiseNotesWindow()
    {
        if (_notesWindow == null) return;

        if (_notesWindow.WindowState == WindowState.Minimized)
            _notesWindow.WindowState = WindowState.Normal;

        _notesWindow.Show();
        _notesWindow.Activate();
    }

    public void ShowBalloon(string title, string message)
    {
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = message;
        _trayIcon.ShowBalloonTip(3000);
    }

    /// <summary>Swaps the tray icon, disposing the one it replaces.</summary>
    private void SetTrayIcon(bool active)
    {
        var previous = _trayIcon.Icon;
        _trayIcon.Icon = DrawTrayIcon(active);
        previous?.Dispose();
    }

    // ===== The mark =====
    //
    // One tapered cup, drawn as strokes on a 24×24 grid: an overhanging rim, walls that draw in
    // toward the base, a single round handle, and steam that only appears when caffeine is on.
    // Every appearance of the logo — tray, taskbar, Alt-Tab, the big toggle — renders from these
    // same paths, so the mark can never drift between places. MainWindow.xaml holds the identical
    // geometry for the toggle button; keep the two in step.

    /// <summary>
    /// One continuous stroke: rim, both tapering walls and the rounded base. No handle — the
    /// silhouette is symmetric, which is what lets it centre cleanly in a circle.
    /// </summary>
    public const string MarkBody =
        "M4.6,8.2 L6.0,16.4 C6.2,17.6 7.0,18.2 8.1,18.2 H11.9 C13.0,18.2 13.8,17.6 14.0,16.4 L15.4,8.2 Z";

    /// <summary>
    /// Two wisps of vapour, mirrored about the cup's centre at x = 10. Each leans one way and then
    /// the other over its length, which is what reads as steam rather than as a squiggle.
    /// </summary>
    public const string MarkSteamLeft = "M8.2,6.4 C6.9,4.9 9.5,4.0 8.2,2.0";
    public const string MarkSteamRight = "M11.8,6.4 C10.5,4.9 13.1,4.0 11.8,2.0";

    private const double MarkGrid = 24.0;

    // The cup spans x 4.6–15.4, so its own centre is 10.0 and it needs +2 to sit on the grid
    // centre. Vertically the steam occupies the top of the grid, so the cup alone rides higher.
    public const double MarkNudgeX = 2.0;
    public const double MarkNudgeYSteaming = 1.95;
    public const double MarkNudgeYIdle = -1.2;

    private static readonly Media.Color MarkActive = Media.Color.FromRgb(0x0A, 0x84, 0xFF);
    private static readonly Media.Color MarkIdle = Media.Color.FromRgb(0x8E, 0x8E, 0x93);

    private static void DrawMark(Media.DrawingContext dc, double size, Media.Color colour, bool steaming)
    {
        var scale = size / MarkGrid;
        dc.PushTransform(new Media.ScaleTransform(scale, scale));

        dc.PushTransform(new Media.TranslateTransform(
            MarkNudgeX, steaming ? MarkNudgeYSteaming : MarkNudgeYIdle));

        var brush = new Media.SolidColorBrush(colour);
        var pen = new Media.Pen(brush, 1.6)
        {
            StartLineCap = Media.PenLineCap.Round,
            EndLineCap = Media.PenLineCap.Round,
            LineJoin = Media.PenLineJoin.Round
        };

        dc.DrawGeometry(null, pen, Media.Geometry.Parse(MarkBody));

        if (steaming)
        {
            var steam = new Media.Pen(
                new Media.SolidColorBrush(Media.Color.FromArgb(150, colour.R, colour.G, colour.B)), 1.25)
            {
                StartLineCap = Media.PenLineCap.Round,
                EndLineCap = Media.PenLineCap.Round
            };

            dc.DrawGeometry(null, steam, Media.Geometry.Parse(MarkSteamLeft));
            dc.DrawGeometry(null, steam, Media.Geometry.Parse(MarkSteamRight));
        }

        dc.Pop();
        dc.Pop();
    }

    private static Imaging.RenderTargetBitmap RenderMark(int size, Media.Color colour, bool steaming)
    {
        var visual = new Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
            DrawMark(dc, size, colour, steaming);

        var bitmap = new Imaging.RenderTargetBitmap(size, size, 96, 96, Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Tray icon: grey cup when idle, accent blue with steam when caffeine is on. Rendered at 32px
    /// and left to the shell to scale, which keeps one code path across DPI settings.
    /// </summary>
    private static Icon DrawTrayIcon(bool active)
    {
        const int size = 32;
        var bitmap = RenderMark(size, active ? MarkActive : MarkIdle, steaming: active);

        var stride = size * 4;
        var pixels = new byte[stride * size];
        bitmap.CopyPixels(pixels, stride, 0);

        using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, size, size),
            System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        bmp.UnlockBits(data);

        // GetHicon hands back an unmanaged handle. Clone into a managed icon, then destroy it —
        // the old code leaked one of these on every activate/deactivate.
        var handle = bmp.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    /// <summary>Window and taskbar icon: the brand cup, always steaming.</summary>
    public static Media.ImageSource CreateWindowIcon() => RenderMark(64, MarkActive, steaming: true);

    public void Quit()
    {
        // Exiting from the tray never closes a window, so flush any in-flight edit here.
        if (_notesView != null)
        {
            _notesView.Flush();
            _notesView.PersistState();
        }

        _todoView?.Flush();
        _todoView?.PersistState();
        _dueCheck.Stop();

        ThemeManager.Shutdown();
        NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS);
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Shutdown();
    }
}
