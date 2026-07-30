using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using CaffeineWin.Todo;
using Microsoft.Win32;

namespace CaffeineWin.Controls;

/// <summary>
/// The whole todo experience: lists on the left, tasks on the right, detail expanding inline.
/// Exactly one instance exists — <c>App</c> owns it and reparents it between the tray window's
/// Todo panel and <see cref="TodoWindow"/>, so <c>Loaded</c> fires more than once.
/// </summary>
public partial class TodoView : UserControl
{
    private const string SettingsPath = @"Software\CaffeineWin";
    private const string SidebarWidthKey = "TodoSidebarWidth";
    private const string SelectedListKey = "TodoSelectedList";

    /// <summary>How long a deleted task stays undoable.</summary>
    private static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(6);

    private static readonly Duration Quick = new(TimeSpan.FromMilliseconds(180));
    private static readonly Duration Settle = new(TimeSpan.FromMilliseconds(260));
    private static readonly CubicEase OutEase = new() { EasingMode = EasingMode.EaseOut };
    private static readonly QuadraticEase SoftEase = new() { EasingMode = EasingMode.EaseInOut };

    private readonly TodoStore _store;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _undoTimer;
    private readonly Dictionary<ScrollViewer, SmoothScroller> _scrollers = new();

    private TaskList? _list;
    private TodoTask? _expanded;
    private TaskList? _pendingListDelete;
    private bool _initialised;
    private bool _suppressListChange;
    private bool _confirmVisible;

    /// <summary>Tasks removed by the last delete, kept whole so Undo can put them back.</summary>
    private List<TodoTask>? _undoBuffer;

    public TodoView(TodoStore store)
    {
        _store = store;
        InitializeComponent();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick += (_, _) => Save();

        _undoTimer = new DispatcherTimer { Interval = UndoWindow };
        _undoTimer.Tick += (_, _) => DismissUndo();

        Loaded += OnLoaded;
    }

    // ===== Host contract =====

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The view is reparented between two hosts, so this runs more than once.
        if (_initialised) return;
        _initialised = true;

        RestoreSidebarWidth();
        RebuildLists();
        SelectStartingList();
        SyncSortMenu();
        BuildListColourMenu();
    }

    /// <summary>Commits any in-flight edit and writes through. Hosts call this before closing.</summary>
    public void Flush()
    {
        _saveTimer.Stop();
        CommitUndo();
        Save();
    }

    /// <summary>Writes the view's own state — sidebar width and the list to reopen on.</summary>
    public void PersistState()
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsPath);

        // A view that was never shown has no measured width — don't overwrite a real one with zero.
        var width = SidebarColumn.ActualWidth;
        if (width > 0) key?.SetValue(SidebarWidthKey, (int)Math.Round(width));

        if (_list != null) key?.SetValue(SelectedListKey, _list.Id);
    }

    /// <summary>Plays the arrival animation. Hosts call this when the view becomes visible.</summary>
    public void AnimateIn()
    {
        SidebarPane.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, Settle) { EasingFunction = SoftEase });
        TasksPane.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, Settle) { EasingFunction = SoftEase });
    }

    /// <summary>Window-level keys. Returns true when the view has consumed the key.</summary>
    public bool HandleKey(KeyEventArgs e)
    {
        if (_confirmVisible)
        {
            if (e.Key != Key.Escape) return false;
            _pendingListDelete = null;
            ShowConfirm(false);
            return true;
        }

        if (e.Key == Key.Escape)
        {
            if (_duePopup is { IsOpen: true }) { _duePopup.IsOpen = false; return true; }
            if (_expanded != null) { Collapse(); return true; }
            if (AddBox.IsKeyboardFocusWithin) { AddBox.Clear(); Keyboard.ClearFocus(); return true; }
            return false;
        }

        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            AddBox.Focus();
            return true;
        }

        // A typing key inside a text box is the text box's business.
        return false;
    }

    // ===== Lists =====

    private void RebuildLists()
    {
        _suppressListChange = true;
        var keep = _list;
        ListsBox.ItemsSource = null;
        ListsBox.ItemsSource = _store.Lists.OrderBy(l => l.Order).ToList();
        _suppressListChange = false;

        if (keep != null && _store.Lists.Contains(keep)) ListsBox.SelectedItem = keep;
    }

    private void SelectStartingList()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsPath);
        var wanted = key?.GetValue(SelectedListKey) as string;

        var list = _store.Lists.FirstOrDefault(l => l.Id == wanted)
                   ?? _store.Lists.OrderBy(l => l.Order).FirstOrDefault();

        ListsBox.SelectedItem = list;
    }

    private void Lists_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressListChange) return;

        _list = ListsBox.SelectedItem as TaskList;
        _expanded = null;
        ListTitle.Text = _list?.DisplayName ?? "";
        AddBox.IsEnabled = _list != null;
        Rebuild();
    }

    private void NewList_Click(object sender, RoutedEventArgs e)
    {
        var list = new TaskList
        {
            Name = "",
            Colour = TaskList.Palette[_store.Lists.Count % TaskList.Palette.Length],
            Order = _store.Lists.Select(l => l.Order).DefaultIfEmpty(-1).Max() + 1
        };

        _store.Lists.Add(list);
        RebuildLists();
        ListsBox.SelectedItem = list;
        Save();

        // A brand new list has no name yet, so go straight into renaming it.
        Dispatcher.BeginInvoke(() => BeginRename(list), DispatcherPriority.Loaded);
    }

    private void RenameList_Click(object sender, RoutedEventArgs e)
    {
        if (ListsBox.SelectedItem is TaskList list) BeginRename(list);
    }

    /// <summary>Swaps the selected row's label for an editor. Committing writes the name back.</summary>
    private void BeginRename(TaskList list)
    {
        ListsBox.SelectedItem = list;
        ListsBox.UpdateLayout();

        if (ListsBox.ItemContainerGenerator.ContainerFromItem(list) is not ListBoxItem row) return;
        if (FindDescendant<TextBlock>(row) is not { } label) return;

        var box = new TextBox
        {
            Text = list.Name,
            FontFamily = label.FontFamily,
            FontSize = label.FontSize,
            Foreground = (Brush)FindResource("PrimaryText"),
            CaretBrush = (Brush)FindResource("PrimaryText"),
            SelectionBrush = (Brush)FindResource("TodoAccent"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Margin = label.Margin,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        if (label.Parent is not Panel host) return;
        var index = host.Children.IndexOf(label);
        host.Children.Remove(label);
        host.Children.Insert(index, box);

        void Finish()
        {
            if (!host.Children.Contains(box)) return;
            list.Name = box.Text.Trim();
            host.Children.Remove(box);
            host.Children.Insert(index, label);
            label.Text = list.DisplayName;
            ListTitle.Text = list.DisplayName;
            Save();
        }

        box.LostFocus += (_, _) => Finish();
        box.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter) { ke.Handled = true; Finish(); }
            else if (ke.Key == Key.Escape) { ke.Handled = true; box.Text = list.Name; Finish(); }
        };

        box.Focus();
        box.SelectAll();
    }

    private void MoveListUp_Click(object sender, RoutedEventArgs e) => MoveList(-1);

    private void MoveListDown_Click(object sender, RoutedEventArgs e) => MoveList(1);

    private void MoveList(int delta)
    {
        if (ListsBox.SelectedItem is not TaskList list) return;

        var ordered = _store.Lists.OrderBy(l => l.Order).ToList();
        var index = ordered.IndexOf(list);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= ordered.Count) return;

        ordered.RemoveAt(index);
        ordered.Insert(target, list);
        for (var i = 0; i < ordered.Count; i++) ordered[i].Order = i;

        RebuildLists();
        ListsBox.SelectedItem = list;
        Save();
    }

    /// <summary>Builds the colour swatches once; the click applies to whichever list is selected.</summary>
    private void BuildListColourMenu()
    {
        foreach (var hex in TaskList.Palette)
        {
            var item = new MenuItem
            {
                Header = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Fill = FromHex(hex),
                    Margin = new Thickness(0, 1, 0, 1)
                },
                Tag = hex
            };

            item.Click += (s, _) =>
            {
                if (ListsBox.SelectedItem is not TaskList list) return;
                list.Colour = (string)((MenuItem)s).Tag;
                RebuildLists();
                Rebuild();
                Save();
            };

            ListColourMenu.Items.Add(item);
        }
    }

    private void DeleteList_Click(object sender, RoutedEventArgs e)
    {
        if (ListsBox.SelectedItem is not TaskList list) return;

        if (_store.Lists.Count == 1)
        {
            ShowError("The last list can't be deleted.");
            return;
        }

        var count = _store.Tasks.Count(t => t.ListId == list.Id);
        _pendingListDelete = list;
        ConfirmTitle.Text = $"Delete “{list.DisplayName}”?";
        ConfirmDetail.Text = count == 0
            ? "This list is empty."
            : count == 1 ? "Its 1 task will be deleted too." : $"Its {count} tasks will be deleted too.";
        ShowConfirm(true);
    }

    private void ConfirmCancel_Click(object sender, RoutedEventArgs e)
    {
        _pendingListDelete = null;
        ShowConfirm(false);
    }

    private void ConfirmDelete_Click(object sender, RoutedEventArgs e)
    {
        ShowConfirm(false);

        var list = _pendingListDelete;
        _pendingListDelete = null;
        if (list == null) return;

        foreach (var task in _store.Tasks.Where(t => t.ListId == list.Id).ToList())
            _store.Tasks.Remove(task);

        _store.Lists.Remove(list);
        _expanded = null;

        RebuildLists();
        ListsBox.SelectedItem = _store.Lists.OrderBy(l => l.Order).FirstOrDefault();
        Save();
    }

    private void ShowConfirm(bool show)
    {
        _confirmVisible = show;

        if (!show)
        {
            var dismiss = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(160))) { EasingFunction = SoftEase };
            // Re-check the flag on completion: a fast cancel-then-open must not hide a fresh overlay.
            dismiss.Completed += (_, _) =>
            {
                if (!_confirmVisible) ConfirmOverlay.Visibility = Visibility.Collapsed;
            };
            ConfirmOverlay.BeginAnimation(OpacityProperty, dismiss);
            return;
        }

        ConfirmOverlay.Visibility = Visibility.Visible;
        ConfirmOverlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, Quick) { EasingFunction = SoftEase });
        ConfirmScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.97, 1, Settle) { EasingFunction = OutEase });
        ConfirmScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.97, 1, Settle) { EasingFunction = OutEase });
    }

    // ===== Sorting =====

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (SortButton.ContextMenu is not { } menu) return;
        menu.PlacementTarget = SortButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void SortMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string tag) return;
        if (!Enum.TryParse<TaskSort>(tag, out var sort)) return;

        TodoSettings.Sort = sort;
        SyncSortMenu();
        Rebuild();
    }

    private void SyncSortMenu()
    {
        var sort = TodoSettings.Sort;
        SortManual.IsChecked = sort == TaskSort.Manual;
        SortDate.IsChecked = sort == TaskSort.Date;
        SortTitle.IsChecked = sort == TaskSort.Title;
    }

    /// <summary>Re-reads settings that Settings can change while this view is alive.</summary>
    public void RefreshSettings()
    {
        SyncSortMenu();
        Rebuild();
    }

    // ===== Adding =====

    private void Add_TextChanged(object sender, TextChangedEventArgs e) =>
        AddPlaceholder.Visibility = AddBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Add_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _list == null) return;
        e.Handled = true;

        var title = AddBox.Text.Trim();
        if (title.Length == 0) return;

        var task = new TodoTask
        {
            ListId = _list.Id,
            Title = title,
            Order = _store.NextOrder(_list.Id, null)
        };

        _store.Tasks.Add(task);
        AddBox.Clear();
        Rebuild();
        Save();
    }

    // ===== Task list =====

    private void Rebuild()
    {
        TaskStack.Children.Clear();

        if (_list == null)
        {
            EmptyLabel.Visibility = Visibility.Collapsed;
            return;
        }

        var sort = TodoSettings.Sort;
        var open = _store.TopLevel(_list.Id, sort, completed: false).ToList();
        var done = _store.TopLevel(_list.Id, sort, completed: true)
                         .OrderByDescending(t => t.CompletedAt ?? DateTime.MinValue).ToList();

        foreach (var task in open) TaskStack.Children.Add(BuildRow(task, subtask: false));

        if (done.Count > 0) TaskStack.Children.Add(BuildCompletedSection(done));

        EmptyLabel.Visibility = open.Count == 0 && done.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private UIElement BuildCompletedSection(List<TodoTask> done)
    {
        var host = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

        var chevron = new Path
        {
            Data = Geometry.Parse("M1,1 L5,5 L9,1"),
            Stroke = (Brush)FindResource("SecondaryText"),
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 10,
            Height = 6,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(TodoSettings.CompletedOpen ? 0 : -90)
        };

        var label = new TextBlock
        {
            Text = $"Completed ({done.Count})",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("SecondaryText"),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var clear = new Button
        {
            Content = "Clear all",
            Style = (Style)FindResource("TodoQuietButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        clear.Click += (_, _) => ClearCompleted(done);

        var headerGrid = new Grid();
        headerGrid.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { chevron, label }
        });
        headerGrid.Children.Add(clear);

        var header = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 6, 6, 6),
            Margin = new Thickness(8, 0, 8, 2),
            Cursor = Cursors.Hand,
            Child = headerGrid
        };

        var body = new StackPanel
        {
            Visibility = TodoSettings.CompletedOpen ? Visibility.Visible : Visibility.Collapsed
        };
        foreach (var task in done) body.Children.Add(BuildRow(task, subtask: false));

        header.MouseLeftButtonUp += (_, _) =>
        {
            TodoSettings.CompletedOpen = !TodoSettings.CompletedOpen;
            var opening = TodoSettings.CompletedOpen;

            chevron.RenderTransform = new RotateTransform(opening ? -90 : 0);
            ((RotateTransform)chevron.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(opening ? 0 : -90, Quick) { EasingFunction = OutEase });

            if (opening)
            {
                body.Visibility = Visibility.Visible;
                body.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Quick) { EasingFunction = SoftEase });
            }
            else
            {
                var fade = new DoubleAnimation(1, 0, Quick) { EasingFunction = SoftEase };
                fade.Completed += (_, _) =>
                {
                    if (!TodoSettings.CompletedOpen) body.Visibility = Visibility.Collapsed;
                };
                body.BeginAnimation(OpacityProperty, fade);
            }
        };

        host.Children.Add(header);
        host.Children.Add(body);
        return host;
    }

    private void ClearCompleted(List<TodoTask> done)
    {
        // Subtasks of a cleared task go with it, completed or not.
        var doomed = done.Concat(done.SelectMany(t => _store.Tasks.Where(c => c.ParentId == t.Id))).Distinct().ToList();
        DeleteTasks(doomed, doomed.Count == 1 ? "Task deleted" : $"{done.Count} tasks deleted");
    }

    // ===== One row =====

    private Border BuildRow(TodoTask task, bool subtask)
    {
        var listColour = FromHex(_store.Lists.FirstOrDefault(l => l.Id == task.ListId)?.Colour ?? TaskList.Palette[0]);
        var compact = TodoSettings.Density == TaskDensity.Compact;
        var pad = compact ? 6.0 : 10.0;

        var tick = BuildTick(task, listColour);

        var title = new TextBlock
        {
            Text = task.DisplayTitle,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = subtask ? 12.5 : 13.5,
            Foreground = (Brush)FindResource(task.Completed ? "SecondaryText" : "PrimaryText"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (task.Completed) title.TextDecorations = TextDecorations.Strikethrough;

        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        AddMeta(meta, task, subtask);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        text.Children.Add(title);
        if (meta.Children.Count > 0) text.Children.Add(meta);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(tick, 0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(tick);
        grid.Children.Add(text);

        var content = new StackPanel();
        content.Children.Add(new Border
        {
            Padding = new Thickness(pad + 2, pad, pad, pad),
            Background = Brushes.Transparent,
            Child = grid
        });

        var card = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(subtask ? 30 : 6, 1, 6, 1),
            Cursor = Cursors.Hand,
            Tag = task,
            Child = content
        };

        card.MouseEnter += (_, _) =>
        {
            if (!ReferenceEquals(_expanded, task)) card.Background = (Brush)FindResource("SurfaceHover");
        };
        card.MouseLeave += (_, _) =>
        {
            if (!ReferenceEquals(_expanded, task)) card.Background = Brushes.Transparent;
        };

        card.MouseLeftButtonUp += (_, e) =>
        {
            if (_dragging) return;
            e.Handled = true;
            Toggle(task);
        };

        card.ContextMenu = BuildRowMenu(task, subtask);
        AttachDrag(card, task, subtask);

        if (ReferenceEquals(_expanded, task))
        {
            card.Background = (Brush)FindResource("SurfaceColor");
            content.Children.Add(BuildDetail(task, subtask));
        }

        return card;
    }

    /// <summary>The tick circle. It carries the list's colour, which is the only place colour appears.</summary>
    private Border BuildTick(TodoTask task, Brush listColour)
    {
        var ring = new Ellipse
        {
            Width = 18,
            Height = 18,
            Stroke = task.Completed ? listColour : (Brush)FindResource("SecondaryText"),
            StrokeThickness = 1.4,
            Fill = task.Completed ? listColour : Brushes.Transparent
        };

        var check = new Path
        {
            Data = Geometry.Parse("M1,4.5 L3.9,7.4 L9.5,1.6"),
            Stroke = Brushes.White,
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 11,
            Height = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = task.Completed ? Visibility.Visible : Visibility.Collapsed
        };

        var host = new Grid { Width = 18, Height = 18, Children = { ring, check } };

        var tick = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(3),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 0, 0),
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            ToolTip = task.Completed ? "Mark as not complete" : "Mark as complete",
            Child = host
        };

        tick.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;

            var scale = (ScaleTransform)tick.RenderTransform;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.8, 1, Settle) { EasingFunction = OutEase });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.8, 1, Settle) { EasingFunction = OutEase });

            SetCompleted(task, !task.Completed);
        };

        return tick;
    }

    /// <summary>Due chip, repeat, notes and subtask indicators — whatever the task actually has.</summary>
    private void AddMeta(Panel host, TodoTask task, bool subtask)
    {
        if (task.HasDue)
        {
            var overdue = task.IsOverdue;
            host.Children.Add(new Border
            {
                Background = overdue ? (Brush)FindResource("TodoOverdue") : (Brush)FindResource("SurfaceHover"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 1, 6, 2),
                Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock
                {
                    Text = task.DueLabel,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 10.5,
                    Foreground = overdue ? Brushes.White : (Brush)FindResource("SecondaryText")
                }
            });
        }

        if (task.Repeats) host.Children.Add(MetaGlyph("M2,6 A4,4 0 1 1 6,10 M2,6 L4,4 M2,6 L0.2,4.2", "Repeats"));
        if (task.HasNotes) host.Children.Add(MetaGlyph("M0.5,1 H9.5 M0.5,4 H9.5 M0.5,7 H6", "Has details"));

        if (subtask) return;

        var kids = _store.Tasks.Count(t => t.ParentId == task.Id);
        if (kids == 0) return;

        var doneKids = _store.Tasks.Count(t => t.ParentId == task.Id && t.Completed);
        host.Children.Add(new TextBlock
        {
            Text = $"{doneKids}/{kids}",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10.5,
            Foreground = (Brush)FindResource("SecondaryText"),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
    }

    private Path MetaGlyph(string data, string tip) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = (Brush)FindResource("SecondaryText"),
        StrokeThickness = 1.2,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        Stretch = Stretch.Uniform,
        Width = 11,
        Height = 11,
        Margin = new Thickness(0, 0, 7, 0),
        VerticalAlignment = VerticalAlignment.Center,
        ToolTip = tip
    };

    // ===== Expanded detail =====

    private void Toggle(TodoTask task)
    {
        if (ReferenceEquals(_expanded, task)) Collapse();
        else { _expanded = task; Rebuild(); ScrollExpandedIntoView(); }
    }

    private void Collapse()
    {
        _expanded = null;
        Rebuild();
    }

    private void ScrollExpandedIntoView() => Dispatcher.BeginInvoke(() =>
    {
        var card = TaskStack.Children.OfType<Border>().FirstOrDefault(b => ReferenceEquals(b.Tag, _expanded));
        card?.BringIntoView();
    }, DispatcherPriority.Loaded);

    private UIElement BuildDetail(TodoTask task, bool subtask)
    {
        var stack = new StackPanel { Margin = new Thickness(40, 0, 12, 10) };

        var notes = new TextBox
        {
            Text = task.Notes,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12.5,
            Foreground = (Brush)FindResource("PrimaryText"),
            CaretBrush = (Brush)FindResource("PrimaryText"),
            SelectionBrush = (Brush)FindResource("TodoAccent"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 0, 0, 6),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 130,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        notes.TextChanged += (_, _) => { task.Notes = notes.Text; SaveSoon(); };

        var notesHost = new Grid();
        var placeholder = new TextBlock
        {
            Text = "Details",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12.5,
            Foreground = (Brush)FindResource("SecondaryText"),
            IsHitTestVisible = false,
            Visibility = task.HasNotes ? Visibility.Collapsed : Visibility.Visible
        };
        notes.TextChanged += (_, _) =>
            placeholder.Visibility = notes.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        notesHost.Children.Add(placeholder);
        notesHost.Children.Add(notes);
        stack.Children.Add(notesHost);

        // chips row: due and repeat
        var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };

        var dueChip = DetailChip(task.HasDue ? task.DueLabel : "Add date/time",
            "M1,3 H13 M3.5,1 V3 M10.5,1 V3 M1,3 V13 H13 V3", task.HasDue);
        dueChip.Click += (_, _) => OpenDuePicker(dueChip, task);
        chips.Children.Add(dueChip);

        if (task.HasDue)
        {
            var repeatChip = DetailChip(task.Repeats ? task.RepeatLabel : "Repeat",
                "M2,6 A4,4 0 1 1 6,10 M2,6 L4,4 M2,6 L0.2,4.2", task.Repeats);
            repeatChip.Click += (_, _) => OpenRepeatMenu(repeatChip, task);
            chips.Children.Add(repeatChip);
        }

        stack.Children.Add(chips);

        // subtasks (one level only, as in Google Tasks)
        if (!subtask)
        {
            var kids = _store.Children(task.Id, completed: false)
                             .Concat(_store.Children(task.Id, completed: true)).ToList();

            if (kids.Count > 0)
            {
                var kidHost = new StackPanel { Margin = new Thickness(-40, 6, -12, 0) };
                foreach (var kid in kids) kidHost.Children.Add(BuildRow(kid, subtask: true));
                stack.Children.Add(kidHost);
            }

            var addSub = new Button
            {
                Content = "+  Subtask",
                Style = (Style)FindResource("TodoQuietButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(-8, 4, 0, 0)
            };
            addSub.Click += (_, _) => AddSubtask(task);
            stack.Children.Add(addSub);
        }

        var delete = new Button
        {
            Content = "Delete",
            Style = (Style)FindResource("TodoQuietButtonStyle"),
            Foreground = (Brush)FindResource("TodoOverdue"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, -8, 0)
        };
        delete.Click += (_, _) => DeleteTask(task);
        stack.Children.Add(delete);

        stack.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Quick) { EasingFunction = SoftEase });
        return stack;
    }

    private Button DetailChip(string text, string glyph, bool filled)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new Path
        {
            Data = Geometry.Parse(glyph),
            Stroke = filled ? (Brush)FindResource("TodoAccent") : (Brush)FindResource("SecondaryText"),
            StrokeThickness = 1.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.Uniform,
            Width = 12,
            Height = 12,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11.5,
            Foreground = (Brush)FindResource(filled ? "PrimaryText" : "SecondaryText"),
            VerticalAlignment = VerticalAlignment.Center
        });

        var chip = new Button
        {
            Content = content,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 6, 0),
            Template = (ControlTemplate)FindResource("ChipTemplate")
        };

        return chip;
    }

    private void AddSubtask(TodoTask parent)
    {
        var kid = new TodoTask
        {
            ListId = parent.ListId,
            ParentId = parent.Id,
            Order = _store.NextOrder(parent.ListId, parent.Id)
        };

        _store.Tasks.Add(kid);
        Rebuild();
        Save();

        // A blank subtask is useless until it's named, so open its editor straight away.
        Dispatcher.BeginInvoke(() => BeginTitleEdit(kid), DispatcherPriority.Loaded);
    }

    /// <summary>Swaps a row's title for an editor. Used for subtasks and Rename.</summary>
    private void BeginTitleEdit(TodoTask task)
    {
        var card = FindCard(task);
        if (card == null) return;
        if (FindDescendant<TextBlock>(card) is not { } label) return;
        if (label.Parent is not Panel host) return;

        var box = new TextBox
        {
            Text = task.Title,
            FontFamily = label.FontFamily,
            FontSize = label.FontSize,
            Foreground = (Brush)FindResource("PrimaryText"),
            CaretBrush = (Brush)FindResource("PrimaryText"),
            SelectionBrush = (Brush)FindResource("TodoAccent"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0)
        };

        var index = host.Children.IndexOf(label);
        host.Children.Remove(label);
        host.Children.Insert(index, box);

        var finished = false;
        void Finish(bool keep)
        {
            if (finished) return;
            finished = true;

            var text = box.Text.Trim();
            // An unnamed task the user walked away from was never really created.
            if (!keep || text.Length == 0)
            {
                if (task.IsEmpty) { _store.Tasks.Remove(task); Rebuild(); Save(); return; }
            }
            else
            {
                task.Title = text;
            }

            Rebuild();
            Save();
        }

        box.LostFocus += (_, _) => Finish(true);
        box.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter) { ke.Handled = true; Finish(true); }
            else if (ke.Key == Key.Escape) { ke.Handled = true; Finish(false); }
        };
        box.PreviewMouseLeftButtonUp += (_, me) => me.Handled = true;

        box.Focus();
        box.SelectAll();
    }

    private Border? FindCard(TodoTask task) =>
        Descendants(TaskStack).OfType<Border>().FirstOrDefault(b => ReferenceEquals(b.Tag, task));

    // ===== Due picker =====

    private Popup? _duePopup;
    private Calendar? _dueCalendar;
    private TextBox? _dueTimeBox;
    private ToggleButton? _dueTimeToggle;
    private TodoTask? _dueTarget;

    /// <summary>Set while the picker is being filled in, so its own controls don't fire back.</summary>
    private bool _syncingPicker;

    private void OpenDuePicker(UIElement anchor, TodoTask task)
    {
        BuildDuePopup();
        _dueTarget = task;

        // Filling these in raises their own change events, which would rebuild the list and pull the
        // anchor out from under the popup before it opened.
        _syncingPicker = true;
        _dueCalendar!.SelectedDate = task.Due?.Date;
        _dueCalendar.DisplayDate = task.Due?.Date ?? DateTime.Today;
        _dueTimeToggle!.IsChecked = task.HasTime;
        _dueTimeBox!.Text = task.HasTime && task.Due != null
            ? task.Due.Value.ToString("HH:mm")
            : $"{TodoSettings.DefaultDueHour:00}:{TodoSettings.DefaultDueMinute:00}";
        _dueTimeBox.IsEnabled = task.HasTime;
        _syncingPicker = false;

        _duePopup!.PlacementTarget = anchor;
        _duePopup.IsOpen = true;
    }

    private void BuildDuePopup()
    {
        if (_duePopup != null) return;

        _dueCalendar = new Calendar
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 4, 0, 0)
        };
        _dueCalendar.SelectedDatesChanged += (_, _) =>
        {
            if (_syncingPicker || _dueTarget == null || _dueCalendar.SelectedDate == null) return;
            ApplyDue(_dueCalendar.SelectedDate.Value);
        };

        var quick = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
        quick.Children.Add(QuickDate("Today", () => DateTime.Today));
        quick.Children.Add(QuickDate("Tomorrow", () => DateTime.Today.AddDays(1)));
        quick.Children.Add(QuickDate("Next week", () => DateTime.Today.AddDays(7)));

        _dueTimeToggle = new ToggleButton
        {
            Content = "Time",
            Cursor = Cursors.Hand,
            Template = (ControlTemplate)FindResource("ChipToggleTemplate"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        _dueTimeToggle.Checked += (_, _) => { if (_dueTimeBox != null) _dueTimeBox.IsEnabled = true; CommitTime(); };
        _dueTimeToggle.Unchecked += (_, _) =>
        {
            if (_dueTimeBox != null) _dueTimeBox.IsEnabled = false;
            if (_syncingPicker || _dueTarget == null) return;
            _dueTarget.HasTime = false;
            AfterDueChange();
        };

        _dueTimeBox = new TextBox
        {
            Width = 54,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11.5,
            Foreground = (Brush)FindResource("PrimaryText"),
            CaretBrush = (Brush)FindResource("PrimaryText"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // A TextBox can't round its own corners, so it sits in the same chip shape as its neighbours.
        var timeChip = new Border
        {
            Background = (Brush)FindResource("SurfaceHover"),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(9, 5, 9, 5),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _dueTimeBox
        };
        _dueTimeBox.LostFocus += (_, _) => CommitTime();
        _dueTimeBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; CommitTime(); } };

        var clear = new Button
        {
            Content = "No date",
            Style = (Style)FindResource("TodoQuietButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        clear.Click += (_, _) =>
        {
            if (_dueTarget == null) return;
            _dueTarget.Due = null;
            _dueTarget.HasTime = false;
            _dueTarget.Repeat = Recurrence.None;
            AfterDueChange();
            CloseDuePopup();
        };

        var timeRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        timeRow.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _dueTimeToggle, timeChip }
        });
        timeRow.Children.Add(clear);

        var body = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
        body.Children.Add(quick);
        body.Children.Add(_dueCalendar);
        body.Children.Add(timeRow);

        _duePopup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = false,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 6,
            PopupAnimation = PopupAnimation.Fade,
            Child = new Border
            {
                Background = (Brush)FindResource("WindowBackground"),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(10),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    ShadowDepth = 2,
                    BlurRadius = 20,
                    Opacity = 0.3
                },
                Child = body
            }
        };
    }

    private Button QuickDate(string label, Func<DateTime> date)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = label,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11.5,
                Foreground = (Brush)FindResource("SecondaryText")
            },
            Cursor = Cursors.Hand,
            Template = (ControlTemplate)FindResource("ChipTemplate"),
            Margin = new Thickness(0, 0, 6, 0)
        };
        button.Click += (_, _) => { ApplyDue(date()); CloseDuePopup(); };
        return button;
    }

    /// <summary>
    /// Closing the popup while the mouse-up is still routing makes WPF release capture and re-deliver
    /// the event into the calendar it is tearing down, which throws inside CalendarItem. Let the
    /// click finish first.
    /// </summary>
    private void CloseDuePopup() => Dispatcher.BeginInvoke(
        () => { if (_duePopup != null) _duePopup.IsOpen = false; }, DispatcherPriority.Input);

    private void ApplyDue(DateTime date)
    {
        if (_dueTarget == null) return;

        var keepTime = _dueTarget.HasTime && _dueTarget.Due != null
            ? _dueTarget.Due.Value.TimeOfDay
            : TimeSpan.Zero;

        _dueTarget.Due = date.Date.Add(keepTime);
        AfterDueChange();
    }

    private void CommitTime()
    {
        if (_syncingPicker || _dueTarget == null || _dueTimeBox == null) return;
        if (_dueTimeToggle?.IsChecked != true) return;

        if (!TimeSpan.TryParse(_dueTimeBox.Text.Trim(), out var time) || time >= TimeSpan.FromDays(1))
        {
            _dueTimeBox.Text = $"{TodoSettings.DefaultDueHour:00}:{TodoSettings.DefaultDueMinute:00}";
            time = new TimeSpan(TodoSettings.DefaultDueHour, TodoSettings.DefaultDueMinute, 0);
        }

        var day = _dueTarget.Due?.Date ?? DateTime.Today;
        _dueTarget.Due = day.Add(time);
        _dueTarget.HasTime = true;
        AfterDueChange();
    }

    private void AfterDueChange()
    {
        Rebuild();
        Save();
    }

    private void OpenRepeatMenu(FrameworkElement anchor, TodoTask task)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };

        foreach (var option in new[] { Recurrence.None, Recurrence.Daily, Recurrence.Weekly, Recurrence.Monthly, Recurrence.Yearly })
        {
            var item = new MenuItem
            {
                Header = option == Recurrence.None ? "Never" : option.ToString(),
                IsCheckable = true,
                IsChecked = task.Repeat == option,
                Tag = option
            };
            item.Click += (s, _) =>
            {
                task.Repeat = (Recurrence)((MenuItem)s).Tag;
                Rebuild();
                Save();
            };
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    // ===== Completing, moving, deleting =====

    private void SetCompleted(TodoTask task, bool completed)
    {
        if (completed && task.Repeats && task.Due != null)
        {
            // A repeating task doesn't finish, it moves on — same as Google Tasks.
            task.Due = TodoTask.NextOccurrence(task.Due.Value, task.Repeat);
            task.Notified = false;
        }
        else
        {
            task.Completed = completed;

            // Completing a parent completes what's under it; nothing half-done should linger.
            if (completed)
                foreach (var kid in _store.Tasks.Where(t => t.ParentId == task.Id)) kid.Completed = true;

            if (completed && ReferenceEquals(_expanded, task)) _expanded = null;
        }

        Rebuild();
        Save();
    }

    private ContextMenu BuildRowMenu(TodoTask task, bool subtask)
    {
        var menu = new ContextMenu();

        var rename = new MenuItem { Header = "Rename" };
        rename.Click += (_, _) =>
        {
            if (!ReferenceEquals(_expanded, task) && !subtask) { _expanded = task; Rebuild(); }
            Dispatcher.BeginInvoke(() => BeginTitleEdit(task), DispatcherPriority.Loaded);
        };
        menu.Items.Add(rename);

        if (!subtask)
        {
            var move = new MenuItem { Header = "Move to" };
            foreach (var target in _store.Lists.OrderBy(l => l.Order).Where(l => l.Id != task.ListId))
            {
                var item = new MenuItem { Header = target.DisplayName, Tag = target };
                item.Click += (s, _) => MoveTask(task, (TaskList)((MenuItem)s).Tag);
                move.Items.Add(item);
            }
            move.IsEnabled = move.Items.Count > 0;
            menu.Items.Add(move);

            var duplicate = new MenuItem { Header = "Duplicate" };
            duplicate.Click += (_, _) => Duplicate(task);
            menu.Items.Add(duplicate);

            var addSub = new MenuItem { Header = "Add subtask" };
            addSub.Click += (_, _) =>
            {
                if (!ReferenceEquals(_expanded, task)) { _expanded = task; Rebuild(); }
                AddSubtask(task);
            };
            menu.Items.Add(addSub);
        }
        else
        {
            var promote = new MenuItem { Header = "Make a task" };
            promote.Click += (_, _) =>
            {
                task.ParentId = null;
                task.Order = _store.NextOrder(task.ListId, null);
                Rebuild();
                Save();
            };
            menu.Items.Add(promote);
        }

        menu.Items.Add(new Separator());

        var delete = new MenuItem { Header = "Delete" };
        delete.Click += (_, _) => DeleteTask(task);
        menu.Items.Add(delete);

        return menu;
    }

    private void MoveTask(TodoTask task, TaskList target)
    {
        task.ListId = target.Id;
        task.ParentId = null;
        task.Order = _store.NextOrder(target.Id, null);

        // Subtasks travel with their parent, or they'd be orphaned in the old list.
        foreach (var kid in _store.Tasks.Where(t => t.ParentId == task.Id)) kid.ListId = target.Id;

        if (ReferenceEquals(_expanded, task)) _expanded = null;
        Rebuild();
        Save();
    }

    private void Duplicate(TodoTask task)
    {
        var copy = new TodoTask
        {
            ListId = task.ListId,
            Title = task.Title,
            Notes = task.Notes,
            Due = task.Due,
            HasTime = task.HasTime,
            Repeat = task.Repeat,
            Order = _store.NextOrder(task.ListId, null)
        };

        _store.Tasks.Add(copy);

        foreach (var kid in _store.Children(task.Id, false).Concat(_store.Children(task.Id, true)))
        {
            _store.Tasks.Add(new TodoTask
            {
                ListId = copy.ListId,
                ParentId = copy.Id,
                Title = kid.Title,
                Notes = kid.Notes,
                Order = kid.Order
            });
        }

        Rebuild();
        Save();
    }

    private void DeleteTask(TodoTask task)
    {
        var doomed = new List<TodoTask> { task };
        doomed.AddRange(_store.Tasks.Where(t => t.ParentId == task.Id));
        DeleteTasks(doomed, "Task deleted");
    }

    /// <summary>Removes tasks and offers Undo. Nothing is written until the undo window closes.</summary>
    private void DeleteTasks(List<TodoTask> doomed, string message)
    {
        if (doomed.Count == 0) return;

        CommitUndo();

        foreach (var task in doomed) _store.Tasks.Remove(task);
        if (_expanded != null && doomed.Contains(_expanded)) _expanded = null;

        _undoBuffer = doomed;
        Rebuild();
        ShowUndo(message);
    }

    private void ShowUndo(string message)
    {
        UndoText.Text = message;
        UndoToast.Visibility = Visibility.Visible;
        UndoToast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Quick) { EasingFunction = SoftEase });
        UndoSlide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(20, 0, Settle) { EasingFunction = OutEase });

        _undoTimer.Stop();
        _undoTimer.Start();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        _undoTimer.Stop();

        if (_undoBuffer != null)
        {
            foreach (var task in _undoBuffer) _store.Tasks.Add(task);
            _undoBuffer = null;
            Rebuild();
            Save();
        }

        HideUndo();
    }

    /// <summary>The undo window has closed — the deletion is now real and worth writing.</summary>
    private void DismissUndo()
    {
        _undoTimer.Stop();
        CommitUndo();
        HideUndo();
    }

    private void CommitUndo()
    {
        if (_undoBuffer == null) return;
        _undoBuffer = null;
        Save();
    }

    private void HideUndo()
    {
        var fade = new DoubleAnimation(1, 0, Quick) { EasingFunction = SoftEase };
        fade.Completed += (_, _) =>
        {
            if (_undoBuffer == null) UndoToast.Visibility = Visibility.Collapsed;
        };
        UndoToast.BeginAnimation(OpacityProperty, fade);
    }

    // ===== Drag to reorder =====

    private Point _dragStart;
    private bool _dragging;
    private TodoTask? _dragTask;
    private Border? _dragCard;

    private void AttachDrag(Border card, TodoTask task, bool subtask)
    {
        card.PreviewMouseLeftButtonDown += (_, e) =>
        {
            // Reordering only means anything in manual order.
            if (TodoSettings.Sort != TaskSort.Manual || task.Completed) return;
            _dragStart = e.GetPosition(TaskScroll);
            _dragTask = task;
            _dragCard = card;
            _dragging = false;
        };

        card.PreviewMouseMove += (_, e) =>
        {
            if (_dragTask == null || e.LeftButton != MouseButtonState.Pressed) return;
            if (!ReferenceEquals(_dragTask, task)) return;

            var now = e.GetPosition(TaskScroll);
            if (!_dragging)
            {
                if (Math.Abs(now.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                _dragging = true;
                card.CaptureMouse();
                card.Opacity = 0.75;
                Panel.SetZIndex(card, 5);
            }

            MoveDuringDrag(card, now);
            e.Handled = true;
        };

        card.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_dragCard != null && _dragCard.IsMouseCaptured) _dragCard.ReleaseMouseCapture();

            if (_dragging)
            {
                card.Opacity = 1;
                Panel.SetZIndex(card, 0);
                card.RenderTransform = null;
                CommitOrder(card);
                e.Handled = true;

                // Swallow the click that ends the drag so the row doesn't also expand.
                Dispatcher.BeginInvoke(() => _dragging = false, DispatcherPriority.Input);
            }

            _dragTask = null;
            _dragCard = null;
        };
    }

    /// <summary>Moves the dragged card within its own panel as the pointer crosses its neighbours.</summary>
    private void MoveDuringDrag(Border card, Point pointer)
    {
        if (card.Parent is not Panel host) return;

        var siblings = host.Children.OfType<Border>().Where(b => b.Tag is TodoTask t && !t.Completed).ToList();
        var index = siblings.IndexOf(card);
        if (index < 0) return;

        for (var i = 0; i < siblings.Count; i++)
        {
            if (i == index) continue;

            var other = siblings[i];
            var top = other.TranslatePoint(new Point(0, 0), TaskScroll).Y;
            var mid = top + other.ActualHeight / 2;

            var crossed = i < index ? pointer.Y < mid : pointer.Y > mid;
            if (!crossed) continue;

            host.Children.Remove(card);
            host.Children.Insert(host.Children.IndexOf(other) + (i < index ? 0 : 1), card);
            return;
        }
    }

    private void CommitOrder(Border card)
    {
        if (card.Parent is not Panel host) return;

        var order = 0;
        foreach (var row in host.Children.OfType<Border>())
            if (row.Tag is TodoTask task && !task.Completed) task.Order = order++;

        Save();
    }

    // ===== Sidebar width =====

    private void RestoreSidebarWidth()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsPath);
        if (key?.GetValue(SidebarWidthKey) is not int width) return;
        if (width < SidebarColumn.MinWidth || width > SidebarColumn.MaxWidth) return;

        SidebarColumn.Width = new GridLength(width);
    }

    private void Splitter_DragCompleted(object sender, DragCompletedEventArgs e) => PersistState();

    // ===== Saving =====

    private void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Save()
    {
        _saveTimer.Stop();
        if (_store.Save()) return;

        ShowError(_store.LastError ?? "Couldn't save tasks.");
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        if (ErrorStrip.Visibility == Visibility.Visible) return;

        ErrorStrip.Visibility = Visibility.Visible;
        ErrorStrip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Quick) { EasingFunction = SoftEase });
    }

    // ===== Helpers =====

    private void SmoothScroll_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer view || view.ScrollableHeight <= 0) return;

        e.Handled = true;

        if (!_scrollers.TryGetValue(view, out var scroller))
        {
            scroller = new SmoothScroller(view);
            _scrollers[view] = scroller;
        }

        scroller.Nudge(e.Delta);
    }

    private static SolidColorBrush FromHex(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject =>
        Descendants(root).OfType<T>().FirstOrDefault();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
