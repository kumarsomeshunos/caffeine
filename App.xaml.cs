using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
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
    private bool _isActive;
    private DateTime _activatedAt;
    private int _timerMinutes;
    private System.Windows.Threading.DispatcherTimer _ticker = null!;
    private bool _stayGreenMode;
    private bool _jiggleForward = true;

    public bool IsActive => _isActive;
    public DateTime ActivatedAt => _activatedAt;
    public int TimerMinutes => _timerMinutes;

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

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open", null, (s, ev) => ShowMainWindow());
        menu.Items.Add("Pomodoro", null, (s, ev) => ShowMainWindow("pomodoro"));
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
            _trayIcon.Icon = DrawTrayIcon(active: true);
            _trayIcon.Text = "Caffeine — Active";
            _ticker.Start();
        }
        else
        {
            NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS);
            _trayIcon.Icon = DrawTrayIcon(active: false);
            _trayIcon.Text = "Caffeine — Inactive";
            _ticker.Stop();
            _timerMinutes = 0;
        }

        _mainWindow?.UpdateState();
    }

    public void SetTimer(int minutes)
    {
        _timerMinutes = minutes;
        if (minutes > 0 && !_isActive)
            SetActive(true);
        _mainWindow?.UpdateState();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_isActive && _timerMinutes > 0)
        {
            var elapsed = DateTime.Now - _activatedAt;
            if (elapsed.TotalMinutes >= _timerMinutes)
            {
                SetActive(false);
                return;
            }
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

    public void ShowBalloon(string title, string message)
    {
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = message;
        _trayIcon.ShowBalloonTip(3000);
    }

    private static Icon DrawTrayIcon(bool active)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var cupColor = active ? Color.DodgerBlue : Color.Gray;
        using var pen = new Pen(cupColor, 1.5f);
        using var brush = new SolidBrush(cupColor);

        g.FillRectangle(brush, 3, 4, 9, 9);
        g.FillPie(brush, 3, 10, 9, 6, 0, 180);

        using var handlePen = new Pen(cupColor, 1.5f);
        g.DrawArc(handlePen, 11, 5, 4, 6, -60, 120);

        if (active)
        {
            using var steamPen = new Pen(Color.FromArgb(180, Color.DodgerBlue), 1f);
            g.DrawCurve(steamPen, new PointF[] { new(6, 3), new(5, 1.5f), new(6, 0) });
            g.DrawCurve(steamPen, new PointF[] { new(9, 3), new(8, 1.5f), new(9, 0) });
        }

        return Icon.FromHandle(bmp.GetHicon());
    }

    public static System.Windows.Media.ImageSource CreateWindowIcon()
    {
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var blue = Color.FromArgb(0, 122, 255);
        using var brush = new SolidBrush(blue);
        using var pen = new Pen(blue, 2.5f);

        g.FillRectangle(brush, 6, 9, 17, 15);
        g.FillPie(brush, 6, 19, 17, 10, 0, 180);

        g.DrawArc(pen, 22, 12, 6, 10, -60, 120);

        using var steamPen = new Pen(Color.FromArgb(160, blue), 1.5f);
        g.DrawCurve(steamPen, new PointF[] { new(11, 8), new(10, 4), new(11, 1) });
        g.DrawCurve(steamPen, new PointF[] { new(16, 8), new(15, 4), new(16, 1) });
        g.DrawCurve(steamPen, new PointF[] { new(21, 8), new(20, 5), new(21, 2) });

        var hIcon = bmp.GetHicon();
        return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
            hIcon, System.Windows.Int32Rect.Empty,
            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
    }

    public void Quit()
    {
        ThemeManager.Shutdown();
        NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS);
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Shutdown();
    }
}
