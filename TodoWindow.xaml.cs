using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CaffeineWin.Controls;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace CaffeineWin;

/// <summary>
/// Optional standalone home for the shared <see cref="TodoView"/>. Todo normally lives as a panel
/// in the tray window; this window exists so it can be popped out and resized freely.
/// </summary>
public partial class TodoWindow : Window
{
    private const string SettingsPath = @"Software\CaffeineWin";
    private const string BoundsKey = "TodoBounds";

    /// <summary>Shadow gutter and corner radius, matching the tray window's shell.</summary>
    private const double ShellMargin = 16;
    private const double ShellRadius = 12;

    private const string MaximiseGlyph = "□";
    private const string RestoreGlyph = "❐";

    private static readonly IEasingFunction SoftEase = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
    private static readonly IEasingFunction OutEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    private TodoView View => ((App)Application.Current).TodoView;

    public TodoWindow()
    {
        InitializeComponent();
        Icon = App.CreateWindowIcon();

        RestoreGeometry();

        Loaded += (_, _) =>
        {
            ((App)Application.Current).AttachTodoTo(TodoHost);
            View.AnimateIn();
            AnimateWindowIn();
        };
    }

    /// <summary>The window eases up rather than snapping into existence, like a panel arriving.</summary>
    private void AnimateWindowIn()
    {
        if (WindowState == WindowState.Maximized) return;

        Shell.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220))) { EasingFunction = SoftEase });

        var grow = new DoubleAnimation(0.97, 1, new Duration(TimeSpan.FromMilliseconds(300))) { EasingFunction = OutEase };
        ShellScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow.Clone());
        ShellScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    // ===== Window chrome =====

    private void Dock_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).DockTodo();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxRestore_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (MaxRestoreGlyph == null) return;

        var maximised = WindowState == WindowState.Maximized;
        MaxRestoreGlyph.Text = maximised ? RestoreGlyph : MaximiseGlyph;
        MaxRestoreButton.ToolTip = maximised ? "Restore" : "Maximise";

        // Maximised means edge-to-edge: no shadow gutter and no rounded corners, or the desktop
        // would show through a 16px gap around the window.
        var radius = maximised ? 0 : ShellRadius;
        Shell.Margin = new Thickness(maximised ? 0 : ShellMargin);
        Shell.CornerRadius = new CornerRadius(radius);
        ShellInner.CornerRadius = new CornerRadius(radius);

    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (View.HandleKey(e))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        View.Flush();
        View.PersistState();
        PersistGeometry();

        ((App)Application.Current).OnTodoWindowClosed();
    }

    // ===== Geometry persistence =====

    private void RestoreGeometry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsPath, false);
        if (key?.GetValue(BoundsKey) is not string raw) return;

        var parts = raw.Split(',');
        if (parts.Length != 5 ||
            !double.TryParse(parts[0], out var x) || !double.TryParse(parts[1], out var y) ||
            !double.TryParse(parts[2], out var w) || !double.TryParse(parts[3], out var h) ||
            w < MinWidth || h < MinHeight || !IsOnAScreen(x, y, w, h))
            return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = x;
        Top = y;
        Width = w;
        Height = h;

        if (parts[4] == "1")
            WindowState = WindowState.Maximized;
    }

    /// <summary>Guards against reopening onto a monitor that is no longer attached.</summary>
    private static bool IsOnAScreen(double x, double y, double w, double h)
    {
        var target = new System.Drawing.Rectangle((int)x, (int)y, (int)w, (int)h);

        foreach (var screen in WinForms.Screen.AllScreens)
            if (screen.WorkingArea.IntersectsWith(target))
                return true;

        return false;
    }

    private void PersistGeometry()
    {
        var maximised = WindowState == WindowState.Maximized;
        var bounds = maximised ? RestoreBounds : new Rect(Left, Top, Width, Height);

        using var key = Registry.CurrentUser.CreateSubKey(SettingsPath);
        key.SetValue(BoundsKey,
            $"{(int)bounds.X},{(int)bounds.Y},{(int)bounds.Width},{(int)bounds.Height},{(maximised ? 1 : 0)}");
    }
}
