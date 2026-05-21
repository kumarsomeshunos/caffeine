using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace CaffeineWin;

public partial class MainWindow : Window
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "CaffeineWin";

    private static readonly Duration AnimDuration = new(TimeSpan.FromMilliseconds(350));
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(250));
    private static readonly IEasingFunction SoftEase = new QuadraticEase { EasingMode = EasingMode.EaseInOut };

    private enum PomodoroPhase { Work, ShortBreak, LongBreak }
    private enum PomTimerState { Idle, Running, Paused }

    private readonly DispatcherTimer _pomTimer;
    private PomTimerState _pomState = PomTimerState.Idle;
    private PomodoroPhase _pomPhase = PomodoroPhase.Work;
    private int _pomCurrentCycle = 1;
    private int _pomWorkMinutes = 25;
    private int _pomShortBreakMinutes = 5;
    private int _pomLongBreakMinutes = 15;
    private int _pomTotalCycles = 4;
    private TimeSpan _pomRemaining;
    private TimeSpan _pomPhaseTotal;

    private string _currentPanel = "caffeine";
    private string _previousTab = "caffeine";
    private bool _isAnimating;
    private bool _lastToggleState;
    private double _scrollTarget;
    private bool _scrollAnimating;

    public MainWindow()
    {
        InitializeComponent();
        Icon = App.CreateWindowIcon();
        AutoStartToggle.IsChecked = IsAutoStartEnabled();

        _pomTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pomTimer.Tick += PomTimer_Tick;
        PomResetToPhase();

        Loaded += (_, _) =>
        {
            InitializeThemeSelection();
            StayGreenToggle.IsChecked = CaffeineApp.StayGreenMode;
            CaffeineStayGreenToggle.IsChecked = CaffeineApp.StayGreenMode;
            UpdateState();
            UpdateModeIndicator();
            PositionSegIndicator(TabCaffeine, false);
            PositionPillIndicator(TimerIndicator, TimerIndicatorX, TimerPanel, GetCheckedButton(TimerPanel), false);
            Dispatcher.BeginInvoke(() =>
            {
                PositionPillIndicator(WorkIndicator, WorkIndicatorX, WorkPanel, GetCheckedButton(WorkPanel), false);
                PositionPillIndicator(ShortIndicator, ShortIndicatorX, ShortPanel, GetCheckedButton(ShortPanel), false);
                PositionPillIndicator(LongIndicator, LongIndicatorX, LongPanel, GetCheckedButton(LongPanel), false);
                PositionPillIndicator(CyclesIndicator, CyclesIndicatorX, CyclesPanel, GetCheckedButton(CyclesPanel), false);
                PositionThemeIndicator(false);
            }, DispatcherPriority.Loaded);

            ThemeManager.ThemeChanged += OnThemeChanged;
        };
        Closed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
        IsVisibleChanged += (_, _) => { if (IsVisible) UpdateState(); };
    }

    private void OnThemeChanged()
    {
        if (!IsLoaded) return;
        RefreshThemeColors();
    }

    private static RadioButton GetCheckedButton(Panel panel)
    {
        foreach (var child in panel.Children)
        {
            if (child is RadioButton rb && rb.IsChecked == true)
                return rb;
        }
        return (RadioButton)panel.Children[0];
    }

    private App CaffeineApp => (App)Application.Current;

    public void ShowTab(string tab)
    {
        if (tab == "pomodoro")
            TabPomodoro.IsChecked = true;
        else
            TabCaffeine.IsChecked = true;
    }

    // ===== Theme =====

    private void InitializeThemeSelection()
    {
        switch (ThemeManager.CurrentSetting)
        {
            case AppTheme.Light: ThemeLight.IsChecked = true; break;
            case AppTheme.Dark: ThemeDark.IsChecked = true; break;
            default: ThemeSystem.IsChecked = true; break;
        }
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;

        var theme = tag switch
        {
            "Light" => AppTheme.Light,
            "Dark" => AppTheme.Dark,
            _ => AppTheme.System
        };

        ThemeManager.ApplyTheme(theme);
        ThemeManager.SavePreference(theme);

        RefreshThemeColors();

        if (ThemePanel != null && ThemeIndicator != null && rb.IsLoaded)
            PositionPillIndicator(ThemeIndicator, ThemeIndicatorX, ThemePanel, rb, true);
    }

    private void RefreshThemeColors()
    {
        var bgColor = _currentPanel == "pomodoro"
            ? (Color)FindResource("PomodoroRedColor")
            : (Color)FindResource("WindowBackgroundColor");

        WindowBg.BeginAnimation(SolidColorBrush.ColorProperty, null);
        InnerBg.BeginAnimation(SolidColorBrush.ColorProperty, null);
        WindowBg.Color = bgColor;
        InnerBg.Color = bgColor;

        var titleColor = _currentPanel == "pomodoro"
            ? Colors.White
            : (Color)FindResource("PrimaryTextColor");
        var brush = TitleText.Foreground as SolidColorBrush;
        if (brush != null && !brush.IsFrozen)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = titleColor;
        }
        else
        {
            TitleText.Foreground = new SolidColorBrush(titleColor);
        }
    }

    private void PositionThemeIndicator(bool animate)
    {
        RadioButton target = ThemeManager.CurrentSetting switch
        {
            AppTheme.Light => ThemeLight,
            AppTheme.Dark => ThemeDark,
            _ => ThemeSystem
        };
        if (target.IsLoaded)
            PositionPillIndicator(ThemeIndicator, ThemeIndicatorX, ThemePanel, target, animate);
    }

    // ===== Stay Green =====

    private void StayGreen_Changed(object sender, RoutedEventArgs e)
    {
        CaffeineApp.StayGreenMode = StayGreenToggle.IsChecked == true;
        CaffeineStayGreenToggle.IsChecked = CaffeineApp.StayGreenMode;
        UpdateModeIndicator();
    }

    private void CaffeineStayGreen_Changed(object sender, RoutedEventArgs e)
    {
        CaffeineApp.StayGreenMode = CaffeineStayGreenToggle.IsChecked == true;
        StayGreenToggle.IsChecked = CaffeineApp.StayGreenMode;
        UpdateModeIndicator();
    }

    private void UpdateModeIndicator()
    {
        if (ModeIndicator != null)
            ModeIndicator.Text = CaffeineApp.StayGreenMode ? "Mode: Stay Green" : "Mode: Standard";
    }

    // ===== Reset Defaults =====

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.ApplyTheme(AppTheme.System);
        ThemeManager.SavePreference(AppTheme.System);
        ThemeSystem.IsChecked = true;
        PositionThemeIndicator(true);

        AutoStartToggle.IsChecked = false;
        SetAutoStart(false);

        CaffeineApp.StayGreenMode = false;
        StayGreenToggle.IsChecked = false;
        CaffeineStayGreenToggle.IsChecked = false;
        UpdateModeIndicator();

        _pomWorkMinutes = 25;
        PomWork25.IsChecked = true;
        _pomShortBreakMinutes = 5;
        PomShort5.IsChecked = true;
        _pomLongBreakMinutes = 15;
        PomLong15.IsChecked = true;
        _pomTotalCycles = 4;
        PomCycles4.IsChecked = true;
        PomKeepAwakeToggle.IsChecked = true;

        if (PomWorkCustomInput != null) PomWorkCustomInput.Visibility = Visibility.Collapsed;
        if (PomShortCustomInput != null) PomShortCustomInput.Visibility = Visibility.Collapsed;
        if (PomLongCustomInput != null) PomLongCustomInput.Visibility = Visibility.Collapsed;

        if (_pomState == PomTimerState.Idle) PomResetToPhase();
    }

    // ===== Animated transitions =====

    private void Tab_Changed(object sender, RoutedEventArgs e)
    {
        if (CaffeinePanel == null || PomodoroPanel == null || SettingsPanel == null) return;
        if (_isAnimating) return;

        var target = TabPomodoro.IsChecked == true ? "pomodoro" : "caffeine";
        if (target == _currentPanel && SettingsPanel.Visibility != Visibility.Visible) return;

        var targetTab = TabPomodoro.IsChecked == true ? TabPomodoro : TabCaffeine;
        PositionSegIndicator(targetTab, true);

        if (SettingsPanel.Visibility == Visibility.Visible)
            _currentPanel = "settings";

        AnimateToPanel(target);
    }

    private void Tab_Clicked(object sender, RoutedEventArgs e)
    {
        if (SettingsPanel == null || SettingsPanel.Visibility != Visibility.Visible) return;
        if (_isAnimating) return;

        var rb = (RadioButton)sender;
        if (rb.IsChecked != true) return;

        var target = sender == TabPomodoro ? "pomodoro" : "caffeine";
        PositionSegIndicator(rb, true);
        _currentPanel = "settings";
        AnimateToPanel(target);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isAnimating) return;

        if (SettingsPanel.Visibility == Visibility.Visible)
        {
            var target = TabPomodoro.IsChecked == true ? "pomodoro" : "caffeine";
            AnimateToPanel(target);
        }
        else
        {
            _previousTab = _currentPanel;
            AnimateToPanel("settings");
        }
    }

    private void AnimateToPanel(string target)
    {
        _isAnimating = true;

        var outPanel = GetPanelByName(_currentPanel);
        var inPanel = GetPanelByName(target);

        var targetColor = target == "pomodoro"
            ? (Color)FindResource("PomodoroRedColor")
            : (Color)FindResource("WindowBackgroundColor");
        var titleColor = target == "pomodoro"
            ? Colors.White
            : (Color)FindResource("PrimaryTextColor");
        var newTitle = target switch { "pomodoro" => "Pomodoro", "settings" => "Settings", _ => "Caffeine" };

        AnimateBgColor(targetColor);
        AnimateTitleChange(newTitle, titleColor);

        var fadeOut = new DoubleAnimation(0, FadeDuration) { EasingFunction = SoftEase };
        fadeOut.Completed += (_, _) =>
        {
            if (outPanel != inPanel)
                outPanel.Visibility = Visibility.Collapsed;

            inPanel.Visibility = Visibility.Visible;

            if (target == "settings")
                Dispatcher.BeginInvoke(PositionAllSettingsIndicators, DispatcherPriority.Loaded);

            var fadeIn = new DoubleAnimation(0, 1, FadeDuration) { EasingFunction = SoftEase };
            fadeIn.Completed += (_, _) =>
            {
                _currentPanel = target;
                _isAnimating = false;
            };
            inPanel.BeginAnimation(OpacityProperty, fadeIn);

            var scaleXIn = new DoubleAnimation(0.95, 1.0, new Duration(TimeSpan.FromMilliseconds(300)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var scaleYIn = new DoubleAnimation(0.95, 1.0, new Duration(TimeSpan.FromMilliseconds(300)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

            var transform = inPanel.RenderTransform as ScaleTransform;
            transform?.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXIn);
            transform?.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYIn);
        };

        var scaleXOut = new DoubleAnimation(0.97, FadeDuration) { EasingFunction = SoftEase };
        var scaleYOut = new DoubleAnimation(0.97, FadeDuration) { EasingFunction = SoftEase };
        var outTransform = outPanel.RenderTransform as ScaleTransform;
        outTransform?.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXOut);
        outTransform?.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYOut);

        outPanel.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void PositionAllSettingsIndicators()
    {
        PositionPillIndicator(ThemeIndicator, ThemeIndicatorX, ThemePanel, GetCheckedButton(ThemePanel), false);
        PositionPillIndicator(WorkIndicator, WorkIndicatorX, WorkPanel, GetCheckedButton(WorkPanel), false);
        PositionPillIndicator(ShortIndicator, ShortIndicatorX, ShortPanel, GetCheckedButton(ShortPanel), false);
        PositionPillIndicator(LongIndicator, LongIndicatorX, LongPanel, GetCheckedButton(LongPanel), false);
        PositionPillIndicator(CyclesIndicator, CyclesIndicatorX, CyclesPanel, GetCheckedButton(CyclesPanel), false);
    }

    private void AnimateBgColor(Color to)
    {
        var anim = new ColorAnimation(to, AnimDuration) { EasingFunction = SoftEase };
        WindowBg.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        InnerBg.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    // ===== Gooey segmented control indicator =====

    private void PositionSegIndicator(RadioButton target, bool animate)
    {
        if (!target.IsLoaded) return;

        var pos = target.TranslatePoint(new Point(0, 0), SegPanel);
        var targetX = pos.X;
        var targetW = target.ActualWidth;

        if (!animate)
        {
            SegIndicatorX.X = targetX;
            SegIndicator.Width = targetW;
            return;
        }

        var currentX = SegIndicatorX.X;
        var currentW = SegIndicator.Width;

        AnimateGooey(SegIndicator, SegIndicatorX, currentX, currentW, targetX, targetW);
    }

    // ===== Gooey pill group indicator =====

    private void PositionPillIndicator(Border indicator, TranslateTransform transform, Panel container, RadioButton target, bool animate)
    {
        if (!target.IsLoaded || !container.IsLoaded) return;

        var pos = target.TranslatePoint(new Point(0, 0), container);
        var targetX = pos.X;
        var targetW = target.ActualWidth;

        if (!animate)
        {
            transform.X = targetX;
            indicator.Width = targetW;
            return;
        }

        var currentX = transform.X;
        var currentW = indicator.Width;

        AnimateGooey(indicator, transform, currentX, currentW, targetX, targetW);
    }

    private static void AnimateGooey(Border indicator, TranslateTransform transform,
        double currentX, double currentW, double targetX, double targetW)
    {
        var leftEdge = Math.Min(currentX, targetX);
        var rightEdge = Math.Max(currentX + currentW, targetX + targetW);
        var stretchedW = rightEdge - leftEdge;

        var totalDuration = TimeSpan.FromMilliseconds(400);
        var stretchTime = KeyTime.FromPercent(0.4);
        var settleTime = KeyTime.FromPercent(1.0);

        var xAnim = new DoubleAnimationUsingKeyFrames { Duration = new Duration(totalDuration) };
        xAnim.KeyFrames.Add(new SplineDoubleKeyFrame(leftEdge, stretchTime,
            new KeySpline(0.4, 0, 0.2, 1)));
        xAnim.KeyFrames.Add(new SplineDoubleKeyFrame(targetX, settleTime,
            new KeySpline(0.2, 0.8, 0.2, 1)));

        var wAnim = new DoubleAnimationUsingKeyFrames { Duration = new Duration(totalDuration) };
        wAnim.KeyFrames.Add(new SplineDoubleKeyFrame(stretchedW, stretchTime,
            new KeySpline(0.4, 0, 0.2, 1)));
        wAnim.KeyFrames.Add(new SplineDoubleKeyFrame(targetW, settleTime,
            new KeySpline(0.2, 0.8, 0.2, 1)));

        transform.BeginAnimation(TranslateTransform.XProperty, xAnim);
        indicator.BeginAnimation(FrameworkElement.WidthProperty, wAnim);
    }

    // ===== Title animation =====

    private void AnimateTitleChange(string newText, Color toColor)
    {
        var brush = TitleText.Foreground as SolidColorBrush;
        if (brush == null || brush.IsFrozen)
        {
            brush = new SolidColorBrush(((SolidColorBrush)TitleText.Foreground).Color);
            TitleText.Foreground = brush;
        }
        var colorAnim = new ColorAnimation(toColor, AnimDuration) { EasingFunction = SoftEase };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);

        var transform = TitleText.RenderTransform as ScaleTransform;
        if (transform == null) return;

        var shrink = new DoubleAnimation(0.85, new Duration(TimeSpan.FromMilliseconds(120)))
        { EasingFunction = SoftEase };
        var fadeOut = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(120)))
        { EasingFunction = SoftEase };

        fadeOut.Completed += (_, _) =>
        {
            TitleText.Text = newText;

            var grow = new DoubleAnimation(1.0, new Duration(TimeSpan.FromMilliseconds(250)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var fadeIn = new DoubleAnimation(1.0, new Duration(TimeSpan.FromMilliseconds(200)))
            { EasingFunction = SoftEase };

            transform.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            TitleText.BeginAnimation(OpacityProperty, fadeIn);
        };

        transform.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        TitleText.BeginAnimation(OpacityProperty, fadeOut);
    }

    // ===== Toggle button animation =====

    private void AnimateToggleButton(bool activating)
    {
        var toggleCircle = (Ellipse)ToggleButton.Template.FindName("ToggleCircle", ToggleButton);
        var powerIcon = (Path)ToggleButton.Template.FindName("PowerIcon", ToggleButton);

        if (toggleCircle != null)
        {
            var targetFillColor = activating
                ? (Color)FindResource("AccentBlueColor")
                : (Color)FindResource("PowerIconFillColor");

            var fill = toggleCircle.Fill as SolidColorBrush;
            if (fill == null || fill.IsFrozen)
            {
                fill = new SolidColorBrush(fill?.Color ?? Colors.Gray);
                toggleCircle.Fill = fill;
            }
            var fillAnim = new ColorAnimation(targetFillColor, new Duration(TimeSpan.FromMilliseconds(350)))
            { EasingFunction = SoftEase };
            fill.BeginAnimation(SolidColorBrush.ColorProperty, fillAnim);
        }

        if (powerIcon != null)
        {
            var targetStrokeColor = activating
                ? Colors.White
                : (Color)FindResource("PowerIconStrokeColor");

            var stroke = powerIcon.Stroke as SolidColorBrush;
            if (stroke == null || stroke.IsFrozen)
            {
                stroke = new SolidColorBrush(stroke?.Color ?? Colors.Gray);
                powerIcon.Stroke = stroke;
            }
            var strokeAnim = new ColorAnimation(targetStrokeColor, new Duration(TimeSpan.FromMilliseconds(350)))
            { EasingFunction = SoftEase };
            stroke.BeginAnimation(SolidColorBrush.ColorProperty, strokeAnim);
        }

        var btnTransform = ToggleButton.RenderTransform as ScaleTransform;
        if (btnTransform != null)
        {
            var pulseUp = new DoubleAnimationUsingKeyFrames
            {
                Duration = new Duration(TimeSpan.FromMilliseconds(450))
            };
            pulseUp.KeyFrames.Add(new SplineDoubleKeyFrame(1.12, KeyTime.FromPercent(0.4),
                new KeySpline(0.4, 0, 0.2, 1)));
            pulseUp.KeyFrames.Add(new SplineDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0),
                new KeySpline(0.2, 0.8, 0.2, 1)));

            btnTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulseUp.Clone());
            btnTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulseUp);
        }
    }

    // ===== Timer ring pulse (pomodoro) =====

    private void PulseTimerRing()
    {
        var pulse = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(370))
        };
        pulse.KeyFrames.Add(new SplineDoubleKeyFrame(1.06, KeyTime.FromPercent(0.4),
            new KeySpline(0.4, 0, 0.2, 1)));
        pulse.KeyFrames.Add(new SplineDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0),
            new KeySpline(0.2, 0.8, 0.2, 1)));

        TimerRingScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse.Clone());
        TimerRingScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    private Border GetPanelByName(string name) => name switch
    {
        "pomodoro" => PomodoroPanel,
        "settings" => SettingsPanel,
        _ => CaffeinePanel
    };

    // ===== Caffeine =====

    public void UpdateState()
    {
        if (!IsLoaded && !IsVisible) return;

        var active = CaffeineApp.IsActive;
        StatusText.Text = active ? "Active" : "Inactive";
        UpdateModeIndicator();

        if (active != _lastToggleState)
        {
            AnimateToggleButton(active);
            _lastToggleState = active;
        }

        UpdateElapsed();
    }

    public void UpdateElapsed()
    {
        if (!IsLoaded && !IsVisible) return;

        if (CaffeineApp.IsActive)
        {
            var elapsed = DateTime.Now - CaffeineApp.ActivatedAt;
            var remaining = CaffeineApp.TimerMinutes > 0
                ? TimeSpan.FromMinutes(CaffeineApp.TimerMinutes) - elapsed
                : TimeSpan.Zero;

            if (CaffeineApp.TimerMinutes > 0 && remaining.TotalSeconds > 0)
                ElapsedText.Text = $"Auto-off in {FormatTime(remaining)}";
            else
                ElapsedText.Text = $"Active for {FormatTime(elapsed)}";
        }
        else
        {
            ElapsedText.Text = "Screen will sleep normally";
        }
    }

    private static string FormatTime(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes:D2}m";
        if (ts.TotalMinutes >= 1)
            return $"{(int)ts.TotalMinutes}m {ts.Seconds:D2}s";
        return $"{ts.Seconds}s";
    }

    private void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        CaffeineApp.ToggleActive();
    }

    private void Timer_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag && int.TryParse(tag, out int minutes))
        {
            CaffeineApp.SetTimer(minutes);
            if (TimerPanel != null && TimerIndicator != null && rb.IsLoaded)
                PositionPillIndicator(TimerIndicator, TimerIndicatorX, TimerPanel, rb, true);
        }
    }

    private void AutoStart_Changed(object sender, RoutedEventArgs e) => SetAutoStart(AutoStartToggle.IsChecked == true);

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(AppName) != null;
    }

    private static void SetAutoStart(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;
        if (enable)
        {
            var exePath = Environment.ProcessPath ?? "";
            if (!string.IsNullOrEmpty(exePath))
                key.SetValue(AppName, exePath);
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }

    // ===== Pomodoro =====

    private void PomResetToPhase()
    {
        var minutes = _pomPhase switch
        {
            PomodoroPhase.Work => _pomWorkMinutes,
            PomodoroPhase.ShortBreak => _pomShortBreakMinutes,
            PomodoroPhase.LongBreak => _pomLongBreakMinutes,
            _ => _pomWorkMinutes
        };
        _pomRemaining = TimeSpan.FromMinutes(minutes);
        _pomPhaseTotal = _pomRemaining;
        PomUpdateDisplay();
    }

    private void PomTimer_Tick(object? sender, EventArgs e)
    {
        _pomRemaining -= TimeSpan.FromSeconds(1);
        if (_pomRemaining <= TimeSpan.Zero)
        {
            _pomRemaining = TimeSpan.Zero;
            PomOnPhaseComplete();
        }
        PomUpdateDisplay();
    }

    private void PomOnPhaseComplete()
    {
        _pomTimer.Stop();
        if (_pomPhase == PomodoroPhase.Work && PomKeepAwakeToggle.IsChecked == true)
            CaffeineApp.SetActive(false);

        PomShowBalloon();
        PlayCompletionBeeps();
        PomAdvancePhase();
        _pomState = PomTimerState.Idle;
        PomStartPauseButton.Content = "Start";
        PomResetToPhase();
    }

    private void PomAdvancePhase()
    {
        if (_pomPhase == PomodoroPhase.Work)
        {
            if (_pomCurrentCycle >= _pomTotalCycles)
            {
                _pomPhase = PomodoroPhase.LongBreak;
                _pomCurrentCycle = 1;
            }
            else
            {
                _pomPhase = PomodoroPhase.ShortBreak;
            }
        }
        else
        {
            if (_pomPhase == PomodoroPhase.LongBreak)
                _pomCurrentCycle = 1;
            else
                _pomCurrentCycle++;
            _pomPhase = PomodoroPhase.Work;
        }
    }

    private void PomShowBalloon()
    {
        var msg = _pomPhase switch
        {
            PomodoroPhase.Work => "Break's over — time to focus!",
            PomodoroPhase.ShortBreak => "Nice work! Take a short break.",
            PomodoroPhase.LongBreak => "Great session! Take a long break.",
            _ => ""
        };
        CaffeineApp.ShowBalloon("Pomodoro", msg);
    }

    private static void PlayCompletionBeeps()
    {
        Task.Run(() =>
        {
            Console.Beep(800, 200);
            Console.Beep(1000, 200);
            Console.Beep(1200, 300);
            System.Threading.Thread.Sleep(400);
            Console.Beep(800, 200);
            Console.Beep(1000, 200);
            Console.Beep(1200, 300);
        });
    }

    private void PomUpdateDisplay()
    {
        if (PomTimeDisplay == null) return;
        PomTimeDisplay.Text = $"{(int)_pomRemaining.TotalMinutes:D2}:{_pomRemaining.Seconds:D2}";
        PomPhaseText.Text = _pomPhase switch
        {
            PomodoroPhase.Work => "Work",
            PomodoroPhase.ShortBreak => "Short Break",
            PomodoroPhase.LongBreak => "Long Break",
            _ => ""
        };
        PomCycleText.Text = $"Cycle {_pomCurrentCycle} of {_pomTotalCycles}";
        PomDrawProgressArc();
    }

    private void PomDrawProgressArc()
    {
        if (_pomPhaseTotal.TotalSeconds <= 0) return;
        var fraction = 1.0 - (_pomRemaining.TotalSeconds / _pomPhaseTotal.TotalSeconds);
        var angle = fraction * 360.0;

        if (angle <= 0)
        {
            ProgressArc.Data = null;
            return;
        }

        const double cx = 90, cy = 90, r = 87;
        var startRad = -90.0 * Math.PI / 180;
        var endRad = (-90.0 + angle) * Math.PI / 180;
        var x1 = cx + r * Math.Cos(startRad);
        var y1 = cy + r * Math.Sin(startRad);
        var x2 = cx + r * Math.Cos(endRad);
        var y2 = cy + r * Math.Sin(endRad);
        var largeArc = angle > 180 ? 1 : 0;

        try { ProgressArc.Data = Geometry.Parse($"M {x1},{y1} A {r},{r} 0 {largeArc} 1 {x2},{y2}"); }
        catch { }
    }

    private void PomStartPause_Click(object sender, RoutedEventArgs e)
    {
        switch (_pomState)
        {
            case PomTimerState.Idle:
                _pomState = PomTimerState.Running;
                PomStartPauseButton.Content = "Pause";
                _pomTimer.Start();
                if (_pomPhase == PomodoroPhase.Work && PomKeepAwakeToggle.IsChecked == true)
                    CaffeineApp.SetActive(true);
                PulseTimerRing();
                break;
            case PomTimerState.Running:
                _pomState = PomTimerState.Paused;
                PomStartPauseButton.Content = "Resume";
                _pomTimer.Stop();
                break;
            case PomTimerState.Paused:
                _pomState = PomTimerState.Running;
                PomStartPauseButton.Content = "Pause";
                _pomTimer.Start();
                PulseTimerRing();
                break;
        }
    }

    private void PomReset_Click(object sender, RoutedEventArgs e)
    {
        _pomTimer.Stop();
        _pomState = PomTimerState.Idle;
        PomStartPauseButton.Content = "Start";
        if (_pomPhase == PomodoroPhase.Work && PomKeepAwakeToggle.IsChecked == true)
            CaffeineApp.SetActive(false);
        PomResetToPhase();
    }

    private void PomSkip_Click(object sender, RoutedEventArgs e)
    {
        _pomTimer.Stop();
        if (_pomPhase == PomodoroPhase.Work && PomKeepAwakeToggle.IsChecked == true)
            CaffeineApp.SetActive(false);
        _pomState = PomTimerState.Idle;
        PomStartPauseButton.Content = "Start";
        PomAdvancePhase();
        PomResetToPhase();
    }

    // ===== Pomodoro settings handlers =====

    private void PomWorkDuration_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag && int.TryParse(tag, out int m))
        {
            _pomWorkMinutes = m;
            if (PomWorkCustomInput != null) PomWorkCustomInput.Visibility = Visibility.Collapsed;
            if (_pomPhase == PomodoroPhase.Work && _pomState == PomTimerState.Idle) PomResetToPhase();
            if (WorkPanel != null && rb.IsLoaded)
                PositionPillIndicator(WorkIndicator, WorkIndicatorX, WorkPanel, rb, true);
        }
    }

    private void PomWorkCustom_Checked(object sender, RoutedEventArgs e)
    {
        PomWorkCustomInput.Visibility = Visibility.Visible;
        PomWorkCustomInput.Focus();
        if (sender is RadioButton rb && WorkPanel != null && rb.IsLoaded)
            PositionPillIndicator(WorkIndicator, WorkIndicatorX, WorkPanel, rb, true);
    }

    private void PomWorkCustomInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(PomWorkCustomInput.Text, out int m) && m > 0)
        {
            _pomWorkMinutes = m;
            if (_pomPhase == PomodoroPhase.Work && _pomState == PomTimerState.Idle) PomResetToPhase();
        }
    }

    private void PomShortBreak_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag && int.TryParse(tag, out int m))
        {
            _pomShortBreakMinutes = m;
            if (PomShortCustomInput != null) PomShortCustomInput.Visibility = Visibility.Collapsed;
            if (_pomPhase == PomodoroPhase.ShortBreak && _pomState == PomTimerState.Idle) PomResetToPhase();
            if (ShortPanel != null && rb.IsLoaded)
                PositionPillIndicator(ShortIndicator, ShortIndicatorX, ShortPanel, rb, true);
        }
    }

    private void PomShortCustom_Checked(object sender, RoutedEventArgs e)
    {
        PomShortCustomInput.Visibility = Visibility.Visible;
        PomShortCustomInput.Focus();
        if (sender is RadioButton rb && ShortPanel != null && rb.IsLoaded)
            PositionPillIndicator(ShortIndicator, ShortIndicatorX, ShortPanel, rb, true);
    }

    private void PomShortCustomInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(PomShortCustomInput.Text, out int m) && m > 0)
        {
            _pomShortBreakMinutes = m;
            if (_pomPhase == PomodoroPhase.ShortBreak && _pomState == PomTimerState.Idle) PomResetToPhase();
        }
    }

    private void PomLongBreak_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag && int.TryParse(tag, out int m))
        {
            _pomLongBreakMinutes = m;
            if (PomLongCustomInput != null) PomLongCustomInput.Visibility = Visibility.Collapsed;
            if (_pomPhase == PomodoroPhase.LongBreak && _pomState == PomTimerState.Idle) PomResetToPhase();
            if (LongPanel != null && rb.IsLoaded)
                PositionPillIndicator(LongIndicator, LongIndicatorX, LongPanel, rb, true);
        }
    }

    private void PomLongCustom_Checked(object sender, RoutedEventArgs e)
    {
        PomLongCustomInput.Visibility = Visibility.Visible;
        PomLongCustomInput.Focus();
        if (sender is RadioButton rb && LongPanel != null && rb.IsLoaded)
            PositionPillIndicator(LongIndicator, LongIndicatorX, LongPanel, rb, true);
    }

    private void PomLongCustomInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(PomLongCustomInput.Text, out int m) && m > 0)
        {
            _pomLongBreakMinutes = m;
            if (_pomPhase == PomodoroPhase.LongBreak && _pomState == PomTimerState.Idle) PomResetToPhase();
        }
    }

    private void PomCycles_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag && int.TryParse(tag, out int c))
        {
            _pomTotalCycles = c;
            if (CyclesPanel != null && rb.IsLoaded)
                PositionPillIndicator(CyclesIndicator, CyclesIndicatorX, CyclesPanel, rb, true);
        }
        PomUpdateDisplay();
    }

    // ===== Window chrome =====

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (SettingsPanel.Visibility == Visibility.Visible)
            {
                var target = TabPomodoro.IsChecked == true ? "pomodoro" : "caffeine";
                if (!_isAnimating) AnimateToPanel(target);
            }
            else
            {
                Hide();
            }
        }
    }

    private void SmoothScroll_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var sv = (ScrollViewer)sender;
        if (!_scrollAnimating)
            _scrollTarget = sv.VerticalOffset;
        _scrollTarget -= e.Delta * 0.4;
        _scrollTarget = Math.Clamp(_scrollTarget, 0, sv.ScrollableHeight);
        if (!_scrollAnimating) AnimateScroll(sv);
    }

    private void AnimateScroll(ScrollViewer sv)
    {
        _scrollAnimating = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var current = sv.VerticalOffset;
            var diff = _scrollTarget - current;
            if (Math.Abs(diff) < 0.5)
            {
                sv.ScrollToVerticalOffset(_scrollTarget);
                timer.Stop();
                _scrollAnimating = false;
                return;
            }
            sv.ScrollToVerticalOffset(current + diff * 0.2);
        };
        timer.Start();
    }
}
