using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using CaffeineWin.Todo;
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
    private bool _pomHeldCaffeine;

    private string _currentPanel = "caffeine";
    private string _previousTab = "caffeine";
    private bool _isAnimating;
    private bool _lastToggleState;
    private bool _syncingTimerPill;
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
            LoadTodoSettings();
            StayGreenToggle.IsChecked = CaffeineApp.StayGreenMode;
            CaffeineStayGreenToggle.IsChecked = CaffeineApp.StayGreenMode;
            UpdateState();
            SetSteam(CaffeineApp.IsActive);
            UpdateModeIndicator();
            PositionSegIndicator(TabCaffeine, false);
            PositionPillIndicator(TimerIndicator, TimerIndicatorX, TimerPanel, GetCheckedButton(TimerPanel), false);
            Dispatcher.BeginInvoke(() =>
            {
                PositionPillIndicator(WorkIndicator, WorkIndicatorX, WorkPanel, GetCheckedButton(WorkPanel), false);
                PositionPillIndicator(ShortIndicator, ShortIndicatorX, ShortPanel, GetCheckedButton(ShortPanel), false);
                PositionPillIndicator(LongIndicator, LongIndicatorX, LongPanel, GetCheckedButton(LongPanel), false);
                PositionPillIndicator(CyclesIndicator, CyclesIndicatorX, CyclesPanel, GetCheckedButton(CyclesPanel), false);
                PositionPillIndicator(DensityIndicator, DensityIndicatorX, DensityPanel, GetCheckedButton(DensityPanel), false);
                PositionPillIndicator(TodoSortIndicator, TodoSortIndicatorX, TodoSortPanel, GetCheckedButton(TodoSortPanel), false);
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
        switch (tab)
        {
            case "pomodoro": TabPomodoro.IsChecked = true; break;
            case "notes": TabNotes.IsChecked = true; break;
            case "todo": TabTodo.IsChecked = true; break;
            default: TabCaffeine.IsChecked = true; break;
        }
    }

    private string CheckedTabName() =>
        TabTodo.IsChecked == true ? "todo" :
        TabNotes.IsChecked == true ? "notes" :
        TabPomodoro.IsChecked == true ? "pomodoro" : "caffeine";

    private RadioButton CheckedTabButton() =>
        TabTodo.IsChecked == true ? TabTodo :
        TabNotes.IsChecked == true ? TabNotes :
        TabPomodoro.IsChecked == true ? TabPomodoro : TabCaffeine;

    /// <summary>Panels that need the window at its larger size, and the strip in the title bar.</summary>
    private static bool IsWidePanel(string panel) => panel is "notes" or "todo";

    private void PopOut_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPanel == "todo") CaffeineApp.PopOutTodo();
        else CaffeineApp.PopOutNotes();
    }

    /// <summary>
    /// While Todo lives in its own window the tab is disabled, for the same reason as Notes:
    /// selecting it would steal the shared view out of that window.
    /// </summary>
    public void SetTodoPoppedOut(bool poppedOut)
    {
        TabTodo.IsEnabled = !poppedOut;
        TabTodo.ToolTip = poppedOut ? "Todo is open in its own window" : "Todo";

        if (poppedOut) PopOutButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// The tab strip keeps its own centred row on the Caffeine, Pomodoro and Settings panels. Notes
    /// needs that row's height for the editor, so there the strip moves up into the title bar, where
    /// its left edge lines up with the floating notes card.
    /// </summary>
    private void PlaceTabStrip(bool inTitleBar)
    {
        var host = inTitleBar ? TabHostTitle : TabHostRow;
        if (ReferenceEquals(TabStrip.Parent, host)) return;

        ((Grid)TabStrip.Parent).Children.Remove(TabStrip);
        host.Children.Add(TabStrip);

        // The indicator is placed from measured layout, so it has to be re-measured after the move.
        Dispatcher.BeginInvoke(() => PositionSegIndicator(CheckedTabButton(), false), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// While Notes lives in its own window the tab is disabled — otherwise selecting it would steal
    /// the shared view out of that window and leave it empty.
    /// </summary>
    public void SetNotesPoppedOut(bool poppedOut)
    {
        TabNotes.IsEnabled = !poppedOut;
        TabNotes.ToolTip = poppedOut ? "Notes is open in its own window" : null;

        if (poppedOut) PopOutButton.Visibility = Visibility.Collapsed;
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

    /// <summary>
    /// Each feature owns the whole window while it is showing: Pomodoro turns it red, Notes turns it
    /// a warm coffee, everything else is the plain window colour.
    /// </summary>
    private Color PanelBackgroundColor(string panel) => panel switch
    {
        "pomodoro" => (Color)FindResource("PomodoroRedColor"),
        "notes" => (Color)FindResource("NotesAmbientColor"),
        "todo" => (Color)FindResource("TodoAmbientColor"),
        _ => (Color)FindResource("WindowBackgroundColor")
    };

    private void RefreshThemeColors()
    {
        var bgColor = PanelBackgroundColor(_currentPanel);

        WindowBg.BeginAnimation(SolidColorBrush.ColorProperty, null);
        InnerBg.BeginAnimation(SolidColorBrush.ColorProperty, null);
        WindowBg.Color = bgColor;
        InnerBg.Color = bgColor;
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

        // Todo preferences reset; tasks and lists are user content and are deliberately left alone.
        TodoSettings.Density = TaskDensity.Comfortable;
        TodoSettings.Sort = TaskSort.Manual;
        TodoSettings.CompletedOpen = true;
        TodoSettings.DefaultDueHour = 9;
        TodoSettings.DefaultDueMinute = 0;
        LoadTodoSettings();
        CaffeineApp.RefreshTodoSettings();

        if (_pomState == PomTimerState.Idle) PomResetToPhase();
    }

    // ===== Animated transitions =====

    private void Tab_Changed(object sender, RoutedEventArgs e)
    {
        if (CaffeinePanel == null || PomodoroPanel == null || SettingsPanel == null || NotesPanel == null) return;
        if (_isAnimating) return;

        var target = CheckedTabName();
        if (target == _currentPanel && SettingsPanel.Visibility != Visibility.Visible) return;

        PositionSegIndicator(CheckedTabButton(), true);

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

        var target = CheckedTabName();
        PositionSegIndicator(rb, true);
        _currentPanel = "settings";
        AnimateToPanel(target);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isAnimating) return;

        if (SettingsPanel.Visibility == Visibility.Visible)
            AnimateToPanel(CheckedTabName());
        else
        {
            _previousTab = _currentPanel;
            AnimateToPanel("settings");
        }
    }

    private void AnimateToPanel(string target)
    {
        _isAnimating = true;

        // Notes transforms this window rather than opening its own: the panel cross-fades exactly
        // like Pomodoro, and the window eases out to a size the editor can live in.
        if (target == "notes") CaffeineApp.AttachNotesTo(NotesHost);
        if (target == "todo") CaffeineApp.AttachTodoTo(TodoHost);

        PlaceTabStrip(IsWidePanel(target));
        AnimateWindowSize(IsWidePanel(target));

        var poppedOut = target == "notes" ? CaffeineApp.NotesPoppedOut
            : target == "todo" && CaffeineApp.TodoPoppedOut;
        PopOutButton.Visibility = IsWidePanel(target) && !poppedOut
            ? Visibility.Visible
            : Visibility.Collapsed;
        PopOutButton.ToolTip = target == "todo"
            ? "Open Todo in its own window"
            : "Open Notes in its own window";

        var outPanel = GetPanelByName(_currentPanel);
        var inPanel = GetPanelByName(target);

        AnimateBgColor(PanelBackgroundColor(target));

        var fadeOut = new DoubleAnimation(0, FadeDuration) { EasingFunction = SoftEase };
        fadeOut.Completed += (_, _) =>
        {
            if (outPanel != inPanel)
                outPanel.Visibility = Visibility.Collapsed;

            inPanel.Visibility = Visibility.Visible;

            if (target == "settings")
                Dispatcher.BeginInvoke(PositionAllSettingsIndicators, DispatcherPriority.Loaded);
            else if (target == "notes")
                CaffeineApp.NotesView.AnimateIn();
            else if (target == "todo")
                CaffeineApp.TodoView.AnimateIn();

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
        PositionPillIndicator(DensityIndicator, DensityIndicatorX, DensityPanel, GetCheckedButton(DensityPanel), false);
        PositionPillIndicator(TodoSortIndicator, TodoSortIndicatorX, TodoSortPanel, GetCheckedButton(TodoSortPanel), false);
    }

    private void AnimateBgColor(Color to)
    {
        var anim = new ColorAnimation(to, AnimDuration) { EasingFunction = SoftEase };
        WindowBg.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        InnerBg.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    // ===== Gooey segmented control indicator =====

    /// <summary>
    /// Moves the indicator onto a tab. Deferred a layout pass on purpose: checking a tab reveals its
    /// label and hides the previous one's, but the template trigger that does it runs *after* the
    /// `Checked` event this is called from — measuring now would size the indicator to the icon alone.
    /// </summary>
    private void PositionSegIndicator(RadioButton target, bool animate) =>
        Dispatcher.BeginInvoke(() => PlaceSegIndicator(target, animate), DispatcherPriority.Loaded);

    private void PlaceSegIndicator(RadioButton target, bool animate)
    {
        if (!target.IsLoaded) return;

        // Widths have just changed for every tab in the strip, so re-measure before reading positions.
        SegPanel.UpdateLayout();

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

    // ===== The mark on the toggle =====
    //
    // The cup and its steam share one 24-unit canvas so they stay in register. Steam is the state
    // signal: it drifts up and fades while caffeine is on, and the whole mark slides down a little
    // to make room for it, which is the same re-centring the tray icon does.

    private static readonly Duration SteamCycle = new(TimeSpan.FromMilliseconds(2600));

    private Canvas? _markHost;
    private Path? _steamLeft;
    private Path? _steamRight;
    private TranslateTransform? _markNudge;
    private TranslateTransform? _steamLeftRise;
    private TranslateTransform? _steamRightRise;

    private bool ResolveMark()
    {
        if (_markNudge != null) return true;

        // FindName is only valid once the template has actually been applied.
        if (!ToggleButton.IsLoaded) return false;
        ToggleButton.ApplyTemplate();

        _markHost = ToggleButton.Template.FindName("MarkHost", ToggleButton) as Canvas;
        _steamLeft = ToggleButton.Template.FindName("SteamLeft", ToggleButton) as Path;
        _steamRight = ToggleButton.Template.FindName("SteamRight", ToggleButton) as Path;
        if (_markHost == null || _steamLeft == null || _steamRight == null) return false;

        // A transform declared inside a template is frozen, so hand each element its own.
        _markNudge = new TranslateTransform(App.MarkNudgeX, App.MarkNudgeYIdle);
        _markHost.RenderTransform = _markNudge;

        _steamLeftRise = new TranslateTransform();
        _steamRightRise = new TranslateTransform();
        _steamLeft.RenderTransform = _steamLeftRise;
        _steamRight.RenderTransform = _steamRightRise;

        return true;
    }

    /// <summary>Starts or stops the steam and re-centres the mark to match.</summary>
    private void SetSteam(bool steaming)
    {
        if (!ResolveMark()) return;

        _markNudge!.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(steaming ? App.MarkNudgeYSteaming : App.MarkNudgeYIdle, AnimDuration)
            { EasingFunction = SoftEase });

        if (steaming)
        {
            // Half a cycle apart, so the two wisps never rise in lockstep.
            StartWisp(_steamLeft!, _steamLeftRise!, TimeSpan.Zero);
            StartWisp(_steamRight!, _steamRightRise!, TimeSpan.FromMilliseconds(1300));
            return;
        }

        StopWisp(_steamLeft!, _steamLeftRise!);
        StopWisp(_steamRight!, _steamRightRise!);
    }

    private static void StartWisp(Path wisp, TranslateTransform rise, TimeSpan offset)
    {
        var drift = new DoubleAnimation(1.2, -1.4, SteamCycle)
        {
            BeginTime = offset,
            RepeatBehavior = RepeatBehavior.Forever
        };

        // Fade in low, thin out at the top: a wisp that simply blinked would read as a glitch.
        var breathe = new DoubleAnimationUsingKeyFrames
        {
            Duration = SteamCycle,
            BeginTime = offset,
            RepeatBehavior = RepeatBehavior.Forever
        };
        breathe.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        breathe.KeyFrames.Add(new SplineDoubleKeyFrame(1, KeyTime.FromPercent(0.35), new KeySpline(0.4, 0, 0.2, 1)));
        breathe.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromPercent(1), new KeySpline(0.4, 0, 0.6, 1)));

        rise.BeginAnimation(TranslateTransform.YProperty, drift);
        wisp.BeginAnimation(OpacityProperty, breathe);
    }

    private static void StopWisp(Path wisp, TranslateTransform rise)
    {
        wisp.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(200))));

        // Hand the value back, or the held animation keeps the wisp parked wherever it stopped.
        rise.BeginAnimation(TranslateTransform.YProperty, null);
        rise.Y = 0;
    }

    // ===== Toggle button animation =====

    private void AnimateToggleButton(bool activating)
    {
        SetSteam(activating);

        var toggleCircle = (Ellipse)ToggleButton.Template.FindName("ToggleCircle", ToggleButton);
        var powerIcon = (Path)ToggleButton.Template.FindName("CupIcon", ToggleButton);

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

        var targetStrokeColor = activating
            ? Colors.White
            : (Color)FindResource("PowerIconStrokeColor");

        // The steam is part of the mark, so it takes the same colour as the cup.
        foreach (var part in new[] { powerIcon, _steamLeft, _steamRight })
        {
            if (part == null) continue;

            var stroke = part.Stroke as SolidColorBrush;
            if (stroke == null || stroke.IsFrozen)
            {
                stroke = new SolidColorBrush(stroke?.Color ?? Colors.Gray);
                part.Stroke = stroke;
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
        "notes" => NotesPanel,
        "todo" => TodoPanel,
        _ => CaffeinePanel
    };

    // ===== Window growth for the Notes panel =====

    private const double CompactWidth = 380;
    private const double CompactHeight = 500;
    private const double NotesWidth = 900;
    private const double NotesHeight = 620;

    /// <summary>
    /// Eases the window between its compact size and the size Notes needs, growing about its own
    /// centre so it does not appear to lurch sideways.
    /// </summary>
    private void AnimateWindowSize(bool expanded)
    {
        var toWidth = expanded ? NotesWidth : CompactWidth;
        var toHeight = expanded ? NotesHeight : CompactHeight;

        if (Math.Abs(ActualWidth - toWidth) < 0.5 && Math.Abs(ActualHeight - toHeight) < 0.5) return;

        var toLeft = Left + (ActualWidth - toWidth) / 2;
        var toTop = Top + (ActualHeight - toHeight) / 2;

        // Only clamp when we can be sure which monitor this is — WorkArea covers the primary, so
        // leaving a window on a secondary display alone beats yanking it across screens.
        var work = SystemParameters.WorkArea;
        var centre = new Point(Left + ActualWidth / 2, Top + ActualHeight / 2);
        if (work.Contains(centre))
        {
            toLeft = Math.Clamp(toLeft, work.Left, Math.Max(work.Left, work.Right - toWidth));
            toTop = Math.Clamp(toTop, work.Top, Math.Max(work.Top, work.Bottom - toHeight));
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(380));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        AnimateWindowMetric(WidthProperty, ActualWidth, toWidth, duration, ease);
        AnimateWindowMetric(HeightProperty, ActualHeight, toHeight, duration, ease);
        AnimateWindowMetric(LeftProperty, Left, toLeft, duration, ease);
        AnimateWindowMetric(TopProperty, Top, toTop, duration, ease);
    }

    private void AnimateWindowMetric(DependencyProperty property, double from, double to,
        Duration duration, IEasingFunction ease)
    {
        var animation = new DoubleAnimation(from, to, duration) { EasingFunction = ease };

        // Hand the value back afterwards: a held animation would override DragMove, leaving the
        // window undraggable once it had been resized.
        animation.Completed += (_, _) =>
        {
            BeginAnimation(property, null);
            SetValue(property, to);
        };

        BeginAnimation(property, animation);
    }

    // ===== Caffeine =====

    public void UpdateState()
    {
        var active = CaffeineApp.IsActive;

        // Whoever ended the session — user, tray, or auto-off — Pomodoro no longer owns it.
        if (!active) _pomHeldCaffeine = false;

        if (!IsLoaded && !IsVisible) return;

        StatusText.Text = active ? "Active" : "Inactive";
        UpdateModeIndicator();
        SyncTimerSelection();

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
            var remaining = CaffeineApp.AutoOffRemaining;

            if (remaining > TimeSpan.Zero)
                ElapsedText.Text = $"Auto-off in {FormatTime(remaining)}";
            else
                ElapsedText.Text = $"Active for {FormatTime(DateTime.Now - CaffeineApp.ActivatedAt)}";
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
        if (_syncingTimerPill) return;

        if (sender is RadioButton rb && rb.Tag is string tag && int.TryParse(tag, out int minutes))
        {
            CaffeineApp.SetTimer(minutes);
            if (TimerPanel != null && TimerIndicator != null && rb.IsLoaded)
                PositionPillIndicator(TimerIndicator, TimerIndicatorX, TimerPanel, rb, true);
        }
    }

    // Keeps the pills honest: deactivating for any reason clears the auto-off window,
    // so the selection must fall back to Off instead of advertising a timer that is gone.
    private void SyncTimerSelection()
    {
        if (TimerPanel == null) return;

        var target = CaffeineApp.TimerMinutes switch
        {
            15 => Timer15,
            30 => Timer30,
            60 => Timer60,
            120 => Timer120,
            _ => TimerOff
        };

        if (target.IsChecked == true) return;

        _syncingTimerPill = true;
        target.IsChecked = true;
        _syncingTimerPill = false;

        if (TimerIndicator != null && target.IsLoaded)
            PositionPillIndicator(TimerIndicator, TimerIndicatorX, TimerPanel, target, true);
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

    // Pomodoro releases only the keep-awake session it started itself. A session the user
    // switched on by hand is theirs, and must survive a work phase ending.
    private void PomAcquireCaffeine()
    {
        if (_pomPhase != PomodoroPhase.Work || PomKeepAwakeToggle.IsChecked != true) return;
        if (CaffeineApp.IsActive) return;

        _pomHeldCaffeine = true;
        CaffeineApp.SetActive(true);
    }

    private void PomReleaseCaffeine()
    {
        if (!_pomHeldCaffeine) return;

        _pomHeldCaffeine = false;
        CaffeineApp.SetActive(false);
    }

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
        PomReleaseCaffeine();

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
                PomAcquireCaffeine();
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
        PomReleaseCaffeine();
        PomResetToPhase();
    }

    private void PomSkip_Click(object sender, RoutedEventArgs e)
    {
        _pomTimer.Stop();
        PomReleaseCaffeine();
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

    // ===== Todo settings handlers =====
    //
    // These write straight to TodoSettings, which is the single copy — the view re-reads it rather
    // than caching, so a change here shows up the next time the list is built.

    private void TodoDensity_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;
        if (!Enum.TryParse<TaskDensity>(tag, out var density)) return;

        Todo.TodoSettings.Density = density;

        if (DensityPanel != null && rb.IsLoaded)
            PositionPillIndicator(DensityIndicator, DensityIndicatorX, DensityPanel, rb, true);

        CaffeineApp.RefreshTodoSettings();
    }

    private void TodoSort_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;
        if (!Enum.TryParse<TaskSort>(tag, out var sort)) return;

        Todo.TodoSettings.Sort = sort;

        if (TodoSortPanel != null && rb.IsLoaded)
            PositionPillIndicator(TodoSortIndicator, TodoSortIndicatorX, TodoSortPanel, rb, true);

        CaffeineApp.RefreshTodoSettings();
    }

    private void TodoCompleted_Changed(object sender, RoutedEventArgs e)
    {
        Todo.TodoSettings.CompletedOpen = TodoCompletedToggle.IsChecked == true;
        CaffeineApp.RefreshTodoSettings();
    }

    private void TodoDueTime_LostFocus(object sender, RoutedEventArgs e)
    {
        if (TimeSpan.TryParse(TodoDueTimeInput.Text.Trim(), out var time) && time < TimeSpan.FromDays(1))
        {
            Todo.TodoSettings.DefaultDueHour = time.Hours;
            Todo.TodoSettings.DefaultDueMinute = time.Minutes;
        }

        // Whether it parsed or not, show what is actually stored.
        TodoDueTimeInput.Text = $"{Todo.TodoSettings.DefaultDueHour:00}:{Todo.TodoSettings.DefaultDueMinute:00}";
        CaffeineApp.RefreshTodoSettings();
    }

    private void LoadTodoSettings()
    {
        (Todo.TodoSettings.Density == TaskDensity.Compact ? DensityCompact : DensityComfortable).IsChecked = true;

        var sortButton = Todo.TodoSettings.Sort switch
        {
            TaskSort.Date => TodoSortDate,
            TaskSort.Title => TodoSortTitle,
            _ => TodoSortManual
        };
        sortButton.IsChecked = true;

        TodoCompletedToggle.IsChecked = Todo.TodoSettings.CompletedOpen;
        TodoDueTimeInput.Text = $"{Todo.TodoSettings.DefaultDueHour:00}:{Todo.TodoSettings.DefaultDueMinute:00}";
    }

    // ===== Window chrome =====

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Let the active feature claim its own shortcuts before the window acts.
        if (_currentPanel == "notes" && CaffeineApp.NotesView.HandleKey(e))
        {
            e.Handled = true;
            return;
        }

        if (_currentPanel == "todo" && CaffeineApp.TodoView.HandleKey(e))
        {
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape) return;

        if (SettingsPanel.Visibility == Visibility.Visible)
        {
            if (!_isAnimating) AnimateToPanel(CheckedTabName());
        }
        else
        {
            Hide();
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
