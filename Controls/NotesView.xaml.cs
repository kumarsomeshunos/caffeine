using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaffeineWin.Notes;
using Microsoft.Win32;

// System.IO.Path is already in scope here, so the shapes come in by alias rather than by namespace.
using Ellipse = System.Windows.Shapes.Ellipse;
using ShapePath = System.Windows.Shapes.Path;

namespace CaffeineWin.Controls;

/// <summary>
/// The notes list and editor. Hosted either as a panel in the tray window or inside
/// <c>NotesWindow</c>; <c>App</c> keeps a single instance and moves it between the two so only one
/// store is ever open over notes.json.
/// </summary>
public partial class NotesView : UserControl
{
    private const string SettingsPath = @"Software\CaffeineWin";
    private const string ListWidthKey = "NotesListWidth";
    private const string SelectedIdKey = "NotesSelectedId";
    private const string FormatBarKey = "NotesFormatBar";

    /// <summary>Height the formatting bar opens to. Fixed so the fold doesn't need a measure pass.</summary>
    private const double FormatBarHeight = 34;

    private const double MinListWidth = 200;
    private const double MaxListWidth = 420;

    private const double BodyFontSize = 14;
    private const double HeadingFontSize = 19;

    /// <summary>Pictures are scaled down to this so a phone screenshot doesn't swamp the editor.</summary>
    private const double MaxImageWidth = 480;

    private static readonly string[] ImageExtensions =
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff" };

    private static readonly IEasingFunction SoftEase = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
    private static readonly IEasingFunction OutEase = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly FontFamily UiFont = new("Segoe UI");

    private readonly NotesStore _store = new();
    private readonly CollectionViewSource _view = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly Dictionary<ScrollViewer, SmoothScroller> _scrollers = new();

    private Note? _activeNote;
    private Note? _pendingDelete;
    private bool _suppressSelectionChange;
    private bool _suppressEditorChange;
    private bool _confirmVisible;
    private bool _initialised;
    private bool _bodyDirty;
    private bool _suppressFormatSync;

    /// <summary>True while the list is showing Recently Deleted instead of live notes.</summary>
    private bool _showingBin;

    public NotesView()
    {
        InitializeComponent();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += SaveTimer_Tick;

        _promptUndoTimer = new DispatcherTimer { Interval = PromptUndoWindow };
        _promptUndoTimer.Tick += (_, _) => DismissPromptUndo();

        _store.Load();

        // Pasting an image is a paste like any other as far as the caret is concerned, so intercept
        // it here rather than guessing at Ctrl+V — this also covers the context menu.
        DataObject.AddPastingHandler(Editor, Editor_Pasting);

        _view.Source = _store.Notes;
        _view.SortDescriptions.Add(new SortDescription(nameof(Note.Pinned), ListSortDirection.Descending));
        _view.SortDescriptions.Add(new SortDescription(nameof(Note.ModifiedAt), ListSortDirection.Descending));
        _view.Filter += View_Filter;
        NotesList.ItemsSource = _view.View;

        UpdateGrouping();
        RestoreListWidth();
        RestoreFormatBar();

        // Rows change height as previews wrap, and the list scrolls — so keep the indicator honest
        // whenever layout settles, except while it is mid-travel.
        NotesList.LayoutUpdated += (_, _) =>
        {
            if (!_indicatorAnimating) MoveRowIndicator(animate: false);
        };

        // Prompt cards are built in code, so their brushes are resolved once and a dictionary swap
        // never reaches them the way DynamicResource does. Rebuild so a theme change lands on them.
        ThemeManager.ThemeChanged += () =>
        {
            if (_activeNote is { IsPrompt: true }) RebuildPrompts();
        };

        // Fires again every time the view is reparented between hosts, so only run once.
        Loaded += (_, _) =>
        {
            if (_initialised) return;
            _initialised = true;

            SelectRestoredNote();
            UpdateBinAffordance();
            ReportStoreError();
        };
    }

    // ===== Host contract =====

    /// <summary>Commits the editor and writes to disk without tearing anything down.</summary>
    public void Flush()
    {
        CommitEditor();
        Save();
    }

    /// <summary>Persists the divider width and which note was open. Hosts call this before closing.</summary>
    public void PersistState()
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsPath);

        // A view that was never shown has no measured width — don't overwrite a real one with zero.
        if (ListColumn.ActualWidth >= MinListWidth)
            key.SetValue(ListWidthKey, (int)ListColumn.ActualWidth, RegistryValueKind.DWord);

        key.SetValue(SelectedIdKey, (NotesList.SelectedItem as Note)?.Id ?? "");
    }

    /// <summary>
    /// Keyboard shortcuts, routed from whichever window hosts the view. Returns true when the key
    /// was consumed; an unconsumed Escape lets the host decide what closing means.
    /// </summary>
    public bool HandleKey(KeyEventArgs e)
    {
        // The viewer sits over everything, so it gets first refusal on keys.
        if (ViewerOpen && HandleViewerKey(e)) return true;

        if (_confirmVisible)
        {
            if (e.Key != Key.Escape) return false;

            _pendingDelete = null;
            ShowConfirm(false);
            return true;
        }

        // P, not N: Ctrl+Shift+N is WPF's own ToggleNumbering, which the editor consumes before this
        // ever runs — the shortcut would silently number a paragraph instead of making a note.
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.P)
        {
            CreatePromptNote();
            return true;
        }

        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;

        if (ctrl && e.Key == Key.N)
        {
            CreateNote();
            return true;
        }

        if (ctrl && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            return true;
        }

        if (e.Key == Key.Delete && NotesList.IsKeyboardFocusWithin)
        {
            RequestDelete();
            return true;
        }

        if (e.Key == Key.Escape && SearchBox.Text.Length > 0)
        {
            SearchBox.Text = "";
            return true;
        }

        // Never close out from under someone mid-sentence.
        return e.Key == Key.Escape && (Editor.IsKeyboardFocusWithin || PromptSurface.IsKeyboardFocusWithin);
    }

    /// <summary>Plays the arrival animation. Hosts call this when the view becomes visible.</summary>
    public void AnimateIn()
    {
        if (_activeNote != null) AnimateEditorIn();
        else AnimateEmptyStateIn();

        // The note restored at startup is selected while this panel is still collapsed, so its row
        // has no measured position yet and the indicator has nowhere to sit. Place it once the
        // panel has actually been laid out.
        Dispatcher.BeginInvoke(() => MoveRowIndicator(animate: false), DispatcherPriority.Loaded);
    }

    // ===== Selection indicator =====
    //
    // The same idea as the tab strip: one indicator travels between rows, stretching to span both
    // the old and new positions before settling. Vertical instead of horizontal, and parented inside
    // the scrolled content so it follows the rows when the list scrolls.

    private Border? _rowIndicator;
    private FrameworkElement? _rowSurface;
    private TranslateTransform? _rowSlide;
    private bool _indicatorShown;
    private bool _indicatorAnimating;

    private bool ResolveIndicator()
    {
        if (_rowIndicator != null && _rowSurface != null && _rowSlide != null) return true;

        // FindName is only valid once the template has actually been applied.
        if (!NotesList.IsLoaded) return false;
        NotesList.ApplyTemplate();

        _rowIndicator = NotesList.Template.FindName("RowIndicator", NotesList) as Border;
        _rowSurface = NotesList.Template.FindName("RowSurface", NotesList) as FrameworkElement;
        if (_rowIndicator == null || _rowSurface == null) return false;

        // A transform declared inside a template is frozen, so hand the indicator its own.
        _rowSlide = new TranslateTransform();
        _rowIndicator.RenderTransform = _rowSlide;
        return true;
    }

    private void MoveRowIndicator(bool animate)
    {
        if (!ResolveIndicator()) return;
        var slide = _rowSlide!;
        var indicator = _rowIndicator!;

        var row = NotesList.ItemContainerGenerator.ContainerFromItem(NotesList.SelectedItem) as ListBoxItem;
        if (row == null || !row.IsVisible || row.ActualHeight <= 0)
        {
            HideRowIndicator();
            return;
        }

        var top = row.TranslatePoint(new Point(0, 0), _rowSurface!).Y;
        var height = row.ActualHeight;

        // First appearance shouldn't fly in from nowhere — settle in place, then fade up.
        if (!_indicatorShown)
        {
            SnapRowIndicator(slide, top, height);
            _indicatorShown = true;
            indicator.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(180))) { EasingFunction = SoftEase });
            return;
        }

        if (!animate)
        {
            if (Math.Abs(slide.Y - top) < 0.5 && Math.Abs(indicator.Height - height) < 0.5) return;
            SnapRowIndicator(slide, top, height);
            return;
        }

        var topEdge = Math.Min(slide.Y, top);
        var bottomEdge = Math.Max(slide.Y + indicator.Height, top + height);

        var duration = new Duration(TimeSpan.FromMilliseconds(400));
        var stretch = KeyTime.FromPercent(0.4);
        var settle = KeyTime.FromPercent(1.0);

        var travel = new DoubleAnimationUsingKeyFrames { Duration = duration };
        travel.KeyFrames.Add(new SplineDoubleKeyFrame(topEdge, stretch, new KeySpline(0.4, 0, 0.2, 1)));
        travel.KeyFrames.Add(new SplineDoubleKeyFrame(top, settle, new KeySpline(0.2, 0.8, 0.2, 1)));

        var stretchHeight = new DoubleAnimationUsingKeyFrames { Duration = duration };
        stretchHeight.KeyFrames.Add(new SplineDoubleKeyFrame(bottomEdge - topEdge, stretch, new KeySpline(0.4, 0, 0.2, 1)));
        stretchHeight.KeyFrames.Add(new SplineDoubleKeyFrame(height, settle, new KeySpline(0.2, 0.8, 0.2, 1)));

        _indicatorAnimating = true;
        stretchHeight.Completed += (_, _) => _indicatorAnimating = false;

        slide.BeginAnimation(TranslateTransform.YProperty, travel);
        indicator.BeginAnimation(HeightProperty, stretchHeight);
    }

    private void SnapRowIndicator(TranslateTransform slide, double top, double height)
    {
        slide.BeginAnimation(TranslateTransform.YProperty, null);
        _rowIndicator!.BeginAnimation(HeightProperty, null);
        slide.Y = top;
        _rowIndicator.Height = height;
    }

    private void HideRowIndicator()
    {
        if (_rowIndicator == null || !_indicatorShown) return;

        _indicatorShown = false;
        _rowIndicator.BeginAnimation(OpacityProperty, null);
        _rowIndicator.Opacity = 0;
    }

    // ===== Notes list =====

    private void View_Filter(object sender, FilterEventArgs e)
    {
        if (e.Item is not Note note)
        {
            e.Accepted = false;
            return;
        }

        // The bin and the live list are two views over one collection.
        if (note.IsDeleted != _showingBin)
        {
            e.Accepted = false;
            return;
        }

        // SearchBox is null while the template is still being built.
        var query = SearchBox?.Text.Trim() ?? "";
        e.Accepted = query.Length == 0 ||
                     note.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     note.PlainText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Section headers only earn their space once something is pinned — Apple Notes shows a
    /// bare list until then.
    /// </summary>
    private void UpdateGrouping()
    {
        // Pinning is meaningless in the bin, so it never groups there.
        var wantGroups = !_showingBin && _store.Notes.Any(n => n is { Pinned: true, IsDeleted: false });
        var hasGroups = _view.GroupDescriptions.Count > 0;
        if (wantGroups == hasGroups) return;

        if (wantGroups)
            _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Note.GroupKey)));
        else
            _view.GroupDescriptions.Clear();
    }

    /// <summary>
    /// Re-applies sort and filter. Deliberately not called while typing: live re-sorting would
    /// yank the row you are editing to the top of the list mid-keystroke.
    /// </summary>
    private void RefreshView()
    {
        var keep = NotesList.SelectedItem as Note;

        _suppressSelectionChange = true;
        _view.View.Refresh();
        NotesList.SelectedItem = keep;
        _suppressSelectionChange = false;

        if (!ReferenceEquals(NotesList.SelectedItem, _activeNote))
            SyncActiveNoteToSelection();

        UpdateNoResults();
    }

    private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChange) return;

        var incoming = NotesList.SelectedItem as Note;

        LeaveActiveNote();

        // Discarding an empty note can disturb the list's own selection — re-assert it.
        _suppressSelectionChange = true;
        NotesList.SelectedItem = incoming;
        _suppressSelectionChange = false;

        _activeNote = incoming;
        LoadActiveNote();
    }

    private void SyncActiveNoteToSelection()
    {
        // This changes the active note, so it has to commit the outgoing one first: a refresh can
        // drop the selection mid-debounce — search for text the open note doesn't match — and
        // LoadActiveNote would then clear the dirty flags with the write still pending.
        CommitEditor();

        _activeNote = NotesList.SelectedItem as Note;
        LoadActiveNote();
    }

    /// <summary>Commits whatever is in the editor, then drops the note if it was left blank.</summary>
    private void LeaveActiveNote()
    {
        if (_activeNote == null) return;

        var leaving = _activeNote;
        PruneBlankPrompts();
        CommitEditor();
        _activeNote = null;

        // A prompt set we could not read looks empty because the load failed, not because it is —
        // discarding on that would delete a note over a transient file lock.
        var unreadable = leaving.IsPrompt && _promptsReadOnly;

        // Only discard blanks from the live list — a deleted note is a record to restore, not a draft.
        if (leaving.IsEmpty && !leaving.IsDeleted && !unreadable)
        {
            _suppressSelectionChange = true;
            _store.Notes.Remove(leaving);
            _suppressSelectionChange = false;

            // The debounce may already have written a file for prompts typed and then cleared.
            if (leaving.IsPrompt) _store.DeletePrompts(leaving.Id);

            UpdateGrouping();
        }

        Save();
    }

    private void LoadActiveNote()
    {
        // Undo belongs to the prompt set it was raised from, so it cannot survive a note switch.
        DismissPromptUndo();

        _suppressEditorChange = true;
        TitleBox.Text = _activeNote?.Title ?? "";
        LoadBodyDocument(_activeNote);
        _suppressEditorChange = false;
        _bodyDirty = false;

        SyncEditorMode();
        SyncFormatBar();
        UpdateEditorHeader();
        UpdateEmptyState();
        UpdatePlaceholders();
        UpdateNoteCommands();
        MoveRowIndicator(animate: true);

        if (_activeNote != null) AnimateEditorIn();
    }

    private void SelectFirstAvailable()
    {
        var first = _view.View.Cast<Note>().FirstOrDefault();

        _suppressSelectionChange = true;
        NotesList.SelectedItem = first;
        _suppressSelectionChange = false;

        _activeNote = first;
        LoadActiveNote();
    }

    // ===== Editing =====

    private void Title_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholders();

        if (_suppressEditorChange || _activeNote == null) return;

        // Straight through, so the row's title tracks your typing.
        _activeNote.Title = TitleBox.Text;

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholders();

        if (_suppressEditorChange || _activeNote == null) return;

        // The plain-text mirror updates immediately so the row preview tracks your typing; the
        // timestamp waits for the debounce so the row does not jump while you write.
        _activeNote.PlainText = ReadPlainText();
        _bodyDirty = true;

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveTimer_Tick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        if (_activeNote == null) return;

        _activeNote.ModifiedAt = DateTime.Now;
        WriteActiveBody(_activeNote);
        UpdateEditorHeader();
        Save();
    }

    private void CommitEditor()
    {
        var pending = _saveTimer.IsEnabled;
        _saveTimer.Stop();

        if (_activeNote == null) return;

        _activeNote.Title = TitleBox.Text;

        // A prompt set we could not read leaves _prompts empty, and mirroring that into the index
        // would erase the record of what is actually in the file.
        if (!_activeNote.IsPrompt) _activeNote.PlainText = ReadPlainText();
        else if (!_promptsReadOnly) _activeNote.PlainText = ReadPromptText();

        WriteActiveBody(_activeNote);

        if (pending)
            _activeNote.ModifiedAt = DateTime.Now;
    }

    /// <summary>Writes whichever kind of body the note has — a formatted document or a prompt set.</summary>
    private void WriteActiveBody(Note note)
    {
        if (note.IsPrompt) WritePrompts(note);
        else WriteBody(note);
    }

    // ===== The formatted body =====

    private void LoadBodyDocument(Note? note)
    {
        var document = new FlowDocument { FontFamily = Editor.FontFamily, FontSize = BodyFontSize };
        Editor.Document = document;

        // A prompt note has no document at all; its body is the queue.
        if (note == null || note.IsPrompt) return;

        var opened = _store.LoadBody(note.Id, stream =>
            new TextRange(document.ContentStart, document.ContentEnd).Load(stream, DataFormats.XamlPackage));

        // No body file yet — either a note migrated from the plain-text era, or one whose file has
        // gone missing. Either way the plain-text mirror is the best copy we have.
        if (!opened && !string.IsNullOrEmpty(note.PlainText))
            new TextRange(document.ContentStart, document.ContentEnd).Text = note.PlainText;

        ConstrainImages(document);
    }

    private void WriteBody(Note note)
    {
        if (!_bodyDirty) return;
        _bodyDirty = false;

        note.HasImages = DocumentImages(Editor.Document).Any();

        _store.SaveBody(note.Id, stream =>
            new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd)
                .Save(stream, DataFormats.XamlPackage));

        ReportStoreError();
    }

    private string ReadPlainText() =>
        new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd)
            .Text.Replace("\r\n", "\n").Trim();

    private static IEnumerable<Image> DocumentImages(FlowDocument document)
    {
        for (var position = document.ContentStart;
             position != null && position.CompareTo(document.ContentEnd) < 0;
             position = position.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (position.GetAdjacentElement(LogicalDirection.Forward) is Image image)
                yield return image;
        }
    }

    /// <summary>Keeps loaded pictures inside the editor's width, however large the original was.</summary>
    private void ConstrainImages(FlowDocument document)
    {
        foreach (var image in DocumentImages(document))
            PrepareImage(image);
    }

    /// <summary>
    /// Sizes a picture to the editor and makes it open in the viewer when clicked. Called for both
    /// freshly inserted pictures and ones loaded from a body file.
    /// </summary>
    private void PrepareImage(Image image)
    {
        image.MaxWidth = MaxImageWidth;
        image.Stretch = Stretch.Uniform;
        image.Cursor = Cursors.Hand;
        image.ToolTip = "Click to view full screen";
    }

    /// <summary>
    /// Opens the picture under the pointer. An editable RichTextBox swallows mouse input to elements
    /// embedded in its document, so the click has to be caught on the editor and hit-tested rather
    /// than handled on the Image itself.
    /// </summary>
    private void Editor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (PictureUnder(e.GetPosition(Editor)) is not { Source: BitmapSource source }) return;

        // Handled, or the editor would place the caret behind the viewer.
        e.Handled = true;

        OpenViewer(source);
    }

    /// <summary>
    /// Finds a picture under the pointer through the document rather than the visual tree:
    /// <c>InputHitTest</c> answers a RichTextBox with text elements (a Paragraph is not even a
    /// Visual), and an embedded Image isn't reachable by visual hit-testing either. A text pointer
    /// is the way in — then the pointer is checked against the picture's real bounds, so clicking
    /// the caret position beside a picture doesn't count as clicking it.
    /// </summary>
    private Image? PictureUnder(Point point)
    {
        var position = Editor.GetPositionFromPoint(point, snapToText: true);
        if (position == null) return null;

        var image = position.GetAdjacentElement(LogicalDirection.Forward) as Image
                    ?? position.GetAdjacentElement(LogicalDirection.Backward) as Image;

        if (image == null || image.RenderSize.IsEmpty) return null;

        var origin = image.TranslatePoint(new Point(0, 0), Editor);
        return new Rect(origin, image.RenderSize).Contains(point) ? image : null;
    }

    private void Save()
    {
        _store.Save();
        ReportStoreError();
    }

    // ===== Commands =====

    private void NewNote_Click(object sender, RoutedEventArgs e) => CreateNote();

    private void CreateNote()
    {
        // Reachable by Ctrl+N even though the button is hidden in the bin.
        if (_showingBin) return;

        LeaveActiveNote();

        // A blank note matches no search, so a filtered list would swallow it.
        if (SearchBox.Text.Length > 0)
            SearchBox.Text = "";

        var note = new Note();
        _store.Notes.Add(note);
        UpdateGrouping();

        _suppressSelectionChange = true;
        NotesList.SelectedItem = note;
        _suppressSelectionChange = false;

        _activeNote = note;
        LoadActiveNote();

        NotesList.ScrollIntoView(note);
        TitleBox.Focus();
        Save();
    }

    private void NewPrompt_Click(object sender, RoutedEventArgs e) => CreatePromptNote();

    /// <summary>
    /// The same act as <see cref="CreateNote"/>, but the note it makes holds a queue of prompts
    /// rather than a document. Reachable by Ctrl+Shift+P even though the button is hidden in the bin.
    /// </summary>
    private void CreatePromptNote()
    {
        if (_showingBin) return;

        LeaveActiveNote();

        // A blank note matches no search, so a filtered list would swallow it.
        if (SearchBox.Text.Length > 0)
            SearchBox.Text = "";

        var note = new Note { Kind = NoteKind.Prompt };
        _store.Notes.Add(note);
        UpdateGrouping();

        _suppressSelectionChange = true;
        NotesList.SelectedItem = note;
        _suppressSelectionChange = false;

        _activeNote = note;
        LoadActiveNote();

        // An empty queue has nothing to type into, so it opens with one card waiting.
        AddPrompt(focus: false);

        NotesList.ScrollIntoView(note);

        // The title is the application name, and naming it is the first thing you do.
        TitleBox.Focus();
        Save();
    }

    private void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        if (_showingBin) return;
        if (NotesList.SelectedItem is not Note note) return;

        note.Pinned = !note.Pinned;
        UpdateGrouping();
        RefreshView();
        UpdateNoteCommands();
        NotesList.ScrollIntoView(note);
        Save();
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (NotesList.SelectedItem is not Note note) return;

        CommitEditor();

        var copy = new Note
        {
            Kind = note.Kind,
            Title = note.Title,
            PlainText = note.PlainText,
            HasImages = note.HasImages,
            PromptTotal = note.PromptTotal,
            PromptSent = note.PromptSent
        };

        // The body is a file, so copy it rather than the reference.
        if (note.IsPrompt)
        {
            var prompts = _store.LoadPrompts(note.Id);
            if (prompts != null) _store.SavePrompts(copy.Id, prompts);
        }
        else
        {
            _store.LoadBody(note.Id, source =>
                _store.SaveBody(copy.Id, destination => source.CopyTo(destination)));
        }

        _store.Notes.Add(copy);

        _activeNote = null;
        _suppressSelectionChange = true;
        NotesList.SelectedItem = copy;
        _suppressSelectionChange = false;

        _activeNote = copy;
        LoadActiveNote();
        NotesList.ScrollIntoView(copy);
        Save();
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => RequestDelete();

    /// <summary>
    /// From the live list this is reversible, so it just moves the note to Recently Deleted. From
    /// the bin it is final, and only then is it worth interrupting for.
    /// </summary>
    private void RequestDelete()
    {
        if (NotesList.SelectedItem is not Note note) return;

        if (!_showingBin)
        {
            MoveToBin(note);
            return;
        }

        _pendingDelete = note;
        ConfirmTitle.Text = $"Delete “{note.Title}” for good?";
        ShowConfirm(true);
    }

    private void MoveToBin(Note note)
    {
        _saveTimer.Stop();
        if (ReferenceEquals(_activeNote, note))
            _activeNote = null;

        note.Pinned = false;
        note.DeletedAt = DateTime.Now;

        _suppressSelectionChange = true;
        _view.View.Refresh();
        _suppressSelectionChange = false;

        UpdateGrouping();
        SelectFirstAvailable();
        UpdateBinAffordance();
        Save();
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (NotesList.SelectedItem is not Note note) return;

        _saveTimer.Stop();
        if (ReferenceEquals(_activeNote, note))
            _activeNote = null;

        note.DeletedAt = null;

        _suppressSelectionChange = true;
        _view.View.Refresh();
        _suppressSelectionChange = false;

        UpdateGrouping();
        SelectFirstAvailable();
        UpdateBinAffordance();
        Save();
    }

    private void ToggleBin_Click(object sender, RoutedEventArgs e)
    {
        LeaveActiveNote();

        _showingBin = !_showingBin;

        if (SearchBox.Text.Length > 0)
            SearchBox.Text = "";

        UpdateGrouping();
        HideRowIndicator();

        _suppressSelectionChange = true;
        _view.View.Refresh();
        _suppressSelectionChange = false;

        SelectFirstAvailable();
        UpdateBinAffordance();
    }

    /// <summary>Keeps the toolbar, footer and editor honest about which list is showing.</summary>
    private void UpdateBinAffordance()
    {
        var binned = _store.Notes.Count(n => n.IsDeleted);

        NewNoteButton.Visibility = _showingBin ? Visibility.Collapsed : Visibility.Visible;
        NewPromptButton.Visibility = _showingBin ? Visibility.Collapsed : Visibility.Visible;
        PinButton.Visibility = _showingBin ? Visibility.Collapsed : Visibility.Visible;
        RestoreButton.Visibility = _showingBin ? Visibility.Visible : Visibility.Collapsed;

        DeleteButton.ToolTip = _showingBin ? "Delete permanently (Del)" : "Delete note (Del)";

        BinLabel.Text = _showingBin ? "All Notes" : "Recently Deleted";
        BinCount.Text = _showingBin || binned == 0 ? "" : binned.ToString();
        BinGlyph.Data = Geometry.Parse(_showingBin
            ? "M8.5,2 L3.5,7 L8.5,12"                                          // back chevron
            : "M1.5,3.5 H12.5 M5,3.5 V1.5 H9 V3.5 M3,3.5 V12.5 H11 V3.5");    // bin
        BinGlyph.Stroke = (Brush)FindResource(_showingBin ? "NotesAccent" : "SecondaryText");
        BinLabel.Foreground = (Brush)FindResource(_showingBin ? "NotesAccent" : "SecondaryText");

        // Nothing in the bin and not looking at it? Then the footer is just noise.
        BinButton.Visibility = _showingBin || binned > 0 ? Visibility.Visible : Visibility.Collapsed;

        // A deleted note is a record, not a document — restore it before editing.
        Editor.IsReadOnly = _showingBin;
        TitleBox.IsReadOnly = _showingBin;
        FormatBar.IsEnabled = !_showingBin;
        FormatToggle.IsEnabled = !_showingBin;

        // Nothing to format in the bin, so fold the bar away rather than leave it greyed out.
        if (_showingBin) ShowFormatBar(false, animate: true);
        else RestoreFormatBar();
        SearchPlaceholder.Text = _showingBin ? "Search Recently Deleted" : "Search";
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        if (ErrorStrip.Visibility == Visibility.Visible) return;

        ErrorStrip.Visibility = Visibility.Visible;
        ErrorStrip.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220))) { EasingFunction = SoftEase });
        ErrorSlide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(24, 0, new Duration(TimeSpan.FromMilliseconds(300))) { EasingFunction = OutEase });
    }

    private void ConfirmCancel_Click(object sender, RoutedEventArgs e)
    {
        _pendingDelete = null;
        ShowConfirm(false);
    }

    private void ConfirmDelete_Click(object sender, RoutedEventArgs e)
    {
        ShowConfirm(false);

        var note = _pendingDelete;
        _pendingDelete = null;
        if (note == null) return;

        _saveTimer.Stop();
        if (ReferenceEquals(_activeNote, note))
            _activeNote = null;

        _suppressSelectionChange = true;
        _store.Notes.Remove(note);
        _suppressSelectionChange = false;

        // The index entry is gone; take its body file with it.
        _store.DeleteBody(note.Id);
        _store.DeletePrompts(note.Id);

        UpdateGrouping();
        SelectFirstAvailable();
        UpdateBinAffordance();
        Save();
    }

    private void ShowConfirm(bool show)
    {
        _confirmVisible = show;

        if (!show)
        {
            var dismiss = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(160))) { EasingFunction = SoftEase };
            // Re-check the flag: a fast cancel-then-delete must not collapse a freshly shown overlay.
            dismiss.Completed += (_, _) =>
            {
                if (!_confirmVisible) ConfirmOverlay.Visibility = Visibility.Collapsed;
            };
            ConfirmOverlay.BeginAnimation(OpacityProperty, dismiss);
            return;
        }

        ConfirmOverlay.Visibility = Visibility.Visible;
        ConfirmOverlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(180))) { EasingFunction = SoftEase });

        var pop = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromMilliseconds(260)) };
        pop.KeyFrames.Add(new SplineDoubleKeyFrame(1.03, KeyTime.FromPercent(0.45), new KeySpline(0.4, 0, 0.2, 1)));
        pop.KeyFrames.Add(new SplineDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0), new KeySpline(0.2, 0.8, 0.2, 1)));

        ConfirmScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop.Clone());
        ConfirmScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholders();
        RefreshView();
    }

    // ===== Picture viewer =====
    //
    // Fills the notes surface rather than opening a window of its own: maximise the popped-out
    // notes window and this is full screen.

    private const double MinViewerZoom = 0.05;
    private const double MaxViewerZoom = 8.0;
    private const double ViewerWheelStep = 1.15;
    private const double ViewerButtonStep = 1.25;

    private BitmapSource? _viewing;
    private bool _viewerFitted;
    private Point _panFrom;
    private bool _panning;

    /// <summary>True while the picture viewer is up, so keys and Escape route to it first.</summary>
    private bool ViewerOpen => ViewerOverlay.Visibility == Visibility.Visible;

    private void OpenViewer(BitmapSource source)
    {
        _viewing = source;
        ViewerImage.Source = source;
        ViewerSizeLabel.Text = $"{source.PixelWidth} × {source.PixelHeight}";

        ViewerOverlay.Visibility = Visibility.Visible;
        ViewerOverlay.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(180))) { EasingFunction = SoftEase });

        // Fit once the overlay has actually been measured, or there is no viewport to fit into.
        Dispatcher.BeginInvoke(FitViewer, DispatcherPriority.Loaded);
        ViewerOverlay.Focus();
    }

    private void CloseViewer()
    {
        if (!ViewerOpen) return;

        var fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(150))) { EasingFunction = SoftEase };
        fade.Completed += (_, _) =>
        {
            ViewerOverlay.Visibility = Visibility.Collapsed;
            ViewerImage.Source = null;
            _viewing = null;
        };

        ViewerOverlay.BeginAnimation(OpacityProperty, fade);
        Editor.Focus();
    }

    private void FitViewer()
    {
        if (_viewing == null) return;

        var width = ViewerScroll.ViewportWidth > 0 ? ViewerScroll.ViewportWidth : ViewerScroll.ActualWidth;
        var height = ViewerScroll.ViewportHeight > 0 ? ViewerScroll.ViewportHeight : ViewerScroll.ActualHeight;
        if (width <= 0 || height <= 0) return;

        // Fit shrinks a large picture to fit but never blows a small one up past its real size.
        var scale = Math.Min(width / _viewing.PixelWidth, height / _viewing.PixelHeight);
        SetViewerZoom(Math.Min(scale, 1.0), fitted: true);
    }

    private void SetViewerZoom(double zoom, bool fitted = false)
    {
        if (_viewing == null) return;

        _viewerFitted = fitted;

        var clamped = Math.Clamp(zoom, MinViewerZoom, MaxViewerZoom);
        ViewerZoom.ScaleX = clamped;
        ViewerZoom.ScaleY = clamped;

        ZoomLabel.Text = $"{clamped * 100:0}%";
        ZoomOutButton.IsEnabled = clamped > MinViewerZoom;
        ZoomInButton.IsEnabled = clamped < MaxViewerZoom;

        // Panning only means anything once the picture is larger than the viewport.
        var overflows = clamped * _viewing.PixelWidth > ViewerScroll.ActualWidth ||
                        clamped * _viewing.PixelHeight > ViewerScroll.ActualHeight;
        ViewerScroll.Cursor = overflows ? Cursors.SizeAll : Cursors.Arrow;
    }

    private void ViewerZoomBy(double factor) => SetViewerZoom(ViewerZoom.ScaleX * factor);

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ViewerZoomBy(ViewerButtonStep);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ViewerZoomBy(1 / ViewerButtonStep);
    private void ViewerFit_Click(object sender, RoutedEventArgs e) => FitViewer();
    private void ViewerActualSize_Click(object sender, RoutedEventArgs e) => SetViewerZoom(1.0);
    private void ViewerClose_Click(object sender, RoutedEventArgs e) => CloseViewer();

    private void Viewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;

        // Keep whatever is under the pointer under the pointer.
        var anchor = e.GetPosition(ViewerImage);
        ViewerZoomBy(e.Delta > 0 ? ViewerWheelStep : 1 / ViewerWheelStep);
        ViewerScroll.UpdateLayout();

        var moved = ViewerImage.TranslatePoint(anchor, ViewerScroll);
        var pointer = e.GetPosition(ViewerScroll);
        ViewerScroll.ScrollToHorizontalOffset(ViewerScroll.HorizontalOffset + moved.X - pointer.X);
        ViewerScroll.ScrollToVerticalOffset(ViewerScroll.VerticalOffset + moved.Y - pointer.Y);
    }

    private void Viewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click flips between fitted and actual size, as most viewers do.
            if (_viewerFitted) SetViewerZoom(1.0); else FitViewer();
            e.Handled = true;
            return;
        }

        var origin = ViewerImage.TranslatePoint(new Point(0, 0), ViewerScroll);
        var overPicture = new Rect(origin, ViewerImage.RenderSize).Contains(e.GetPosition(ViewerScroll));

        if (!overPicture)
        {
            // Clicking the surround dismisses — the usual lightbox contract.
            CloseViewer();
            e.Handled = true;
            return;
        }

        _panFrom = e.GetPosition(ViewerScroll);
        _panning = true;
        ViewerScroll.CaptureMouse();
        e.Handled = true;
    }

    private void Viewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;

        var now = e.GetPosition(ViewerScroll);
        ViewerScroll.ScrollToHorizontalOffset(ViewerScroll.HorizontalOffset - (now.X - _panFrom.X));
        ViewerScroll.ScrollToVerticalOffset(ViewerScroll.VerticalOffset - (now.Y - _panFrom.Y));
        _panFrom = now;
    }

    private void Viewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;

        _panning = false;
        ViewerScroll.ReleaseMouseCapture();
    }

    /// <summary>Viewer shortcuts, taken before the notes shortcuts while it is up.</summary>
    private bool HandleViewerKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseViewer();
                return true;
            case Key.Add or Key.OemPlus:
                ViewerZoomBy(ViewerButtonStep);
                return true;
            case Key.Subtract or Key.OemMinus:
                ViewerZoomBy(1 / ViewerButtonStep);
                return true;
            case Key.D0 or Key.NumPad0:
                FitViewer();
                return true;
            case Key.D1 or Key.NumPad1:
                SetViewerZoom(1.0);
                return true;
            default:
                return false;
        }
    }

    // ===== Formatting =====

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e) => SyncFormatBar();

    private void FormatToggle_Click(object sender, RoutedEventArgs e)
    {
        ShowFormatBar(FormatToggle.IsChecked == true, animate: true);

        using var key = Registry.CurrentUser.CreateSubKey(SettingsPath);
        key.SetValue(FormatBarKey, FormatToggle.IsChecked == true ? 1 : 0, RegistryValueKind.DWord);
    }

    /// <summary>Folds the formatting bar in or out above the title.</summary>
    private void ShowFormatBar(bool open, bool animate)
    {
        FormatToggle.IsChecked = open;

        var height = open ? FormatBarHeight : 0;
        var opacity = open ? 1 : 0;

        if (!animate)
        {
            FormatBarHost.BeginAnimation(HeightProperty, null);
            FormatBarHost.BeginAnimation(OpacityProperty, null);
            FormatBarHost.Height = height;
            FormatBarHost.Opacity = opacity;
            return;
        }

        FormatBarHost.BeginAnimation(HeightProperty,
            new DoubleAnimation(height, new Duration(TimeSpan.FromMilliseconds(240))) { EasingFunction = OutEase });
        FormatBarHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(opacity, new Duration(TimeSpan.FromMilliseconds(open ? 220 : 140))) { EasingFunction = SoftEase });
    }

    private void RestoreFormatBar()
    {
        // A prompt note has no document to format, so the bar stays folded whatever is stored.
        if (_activeNote?.IsPrompt == true)
        {
            ShowFormatBar(false, animate: false);
            return;
        }

        using var key = Registry.CurrentUser.OpenSubKey(SettingsPath, false);
        ShowFormatBar(key?.GetValue(FormatBarKey) is int stored && stored == 1, animate: false);
    }

    /// <summary>Tab nests a list item rather than inserting a tab, as it does in Apple Notes.</summary>
    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab) return;
        if (Editor.Selection.Start.Paragraph?.Parent is not ListItem) return;

        if (Keyboard.Modifiers == ModifierKeys.Shift)
            EditingCommands.DecreaseIndentation.Execute(null, Editor);
        else if (Keyboard.Modifiers == ModifierKeys.None)
            EditingCommands.IncreaseIndentation.Execute(null, Editor);
        else
            return;

        e.Handled = true;
    }

    /// <summary>Enter or Tab in the title drops into the body, rather than doing nothing.</summary>
    private void Title_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Tab)) return;

        Editor.Focus();
        e.Handled = true;
    }

    private void Bold_Click(object sender, RoutedEventArgs e) => Apply(EditingCommands.ToggleBold);
    private void Italic_Click(object sender, RoutedEventArgs e) => Apply(EditingCommands.ToggleItalic);
    private void Underline_Click(object sender, RoutedEventArgs e) => Apply(EditingCommands.ToggleUnderline);
    private void Bullets_Click(object sender, RoutedEventArgs e) => Apply(EditingCommands.ToggleBullets);
    private void Numbering_Click(object sender, RoutedEventArgs e) => Apply(EditingCommands.ToggleNumbering);

    private void Apply(RoutedUICommand command)
    {
        command.Execute(null, Editor);
        Editor.Focus();
        SyncFormatBar();
        MarkBodyDirty();
    }

    /// <summary>
    /// Restyling text doesn't raise TextChanged the way typing does — nor does dropping in a
    /// picture — so anything that edits the document without typing has to say so itself.
    /// </summary>
    private void MarkBodyDirty()
    {
        if (_activeNote == null) return;

        _bodyDirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// Underline and strikethrough share one property, so toggling either has to rebuild the
    /// collection rather than overwrite it — otherwise one silently clears the other.
    /// </summary>
    private void Strike_Click(object sender, RoutedEventArgs e)
    {
        var current = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty)
            as TextDecorationCollection;

        var on = HasDecoration(current, TextDecorationLocation.Strikethrough);
        var next = new TextDecorationCollection();

        if (current != null)
            foreach (var decoration in current)
                if (decoration.Location != TextDecorationLocation.Strikethrough)
                    next.Add(decoration);

        if (!on)
            foreach (var decoration in TextDecorations.Strikethrough)
                next.Add(decoration);

        Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,
            next.Count == 0 ? null : next);

        Editor.Focus();
        SyncFormatBar();
        MarkBodyDirty();
    }

    private void Heading_Click(object sender, RoutedEventArgs e)
    {
        var heading = Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double size
                      && size >= HeadingFontSize;

        Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty,
            heading ? BodyFontSize : HeadingFontSize);
        Editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty,
            heading ? FontWeights.Normal : FontWeights.SemiBold);

        Editor.Focus();
        SyncFormatBar();
        MarkBodyDirty();
    }

    private static bool HasDecoration(TextDecorationCollection? decorations, TextDecorationLocation location) =>
        decorations != null && decorations.Any(d => d.Location == location);

    /// <summary>Lights up whichever toggles describe the text under the caret.</summary>
    private void SyncFormatBar()
    {
        if (_suppressFormatSync) return;
        _suppressFormatSync = true;

        BoldButton.IsChecked =
            Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight weight
            && weight >= FontWeights.Bold;

        ItalicButton.IsChecked =
            Editor.Selection.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle style
            && style == FontStyles.Italic;

        var decorations = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty)
            as TextDecorationCollection;
        UnderlineButton.IsChecked = HasDecoration(decorations, TextDecorationLocation.Underline);
        StrikeButton.IsChecked = HasDecoration(decorations, TextDecorationLocation.Strikethrough);

        HeadingButton.IsChecked =
            Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double size
            && size >= HeadingFontSize;

        var marker = (Editor.Selection.Start.Paragraph?.Parent as ListItem)?.List?.MarkerStyle;
        BulletsButton.IsChecked = marker == TextMarkerStyle.Disc;
        NumberingButton.IsChecked = marker == TextMarkerStyle.Decimal;

        _suppressFormatSync = false;
    }

    // ===== Pictures =====

    private void AttachImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add a picture",
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.tif;*.tiff|All files|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true) return;

        foreach (var path in dialog.FileNames)
            InsertImage(LoadImageFile(path));
    }

    private void Editor_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        // Only step in when the clipboard is a picture and nothing else — copying from a document
        // usually carries both, and there the text is what was meant.
        if (Editor.IsReadOnly) return;
        if (!Clipboard.ContainsImage()) return;
        if (Clipboard.ContainsText() || Clipboard.ContainsData(DataFormats.Rtf)) return;

        e.CancelCommand();
        InsertImage(Stabilise(Clipboard.GetImage()));
    }

    private void Editor_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (Editor.IsReadOnly || !DroppedImages(e.Data).Any()) return;

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Editor_PreviewDrop(object sender, DragEventArgs e)
    {
        if (Editor.IsReadOnly) return;

        var files = DroppedImages(e.Data).ToList();
        if (files.Count == 0) return;

        e.Handled = true;

        // Drop where the pointer is, not wherever the caret happened to be.
        var target = Editor.GetPositionFromPoint(e.GetPosition(Editor), snapToText: true);
        if (target != null) Editor.CaretPosition = target;

        foreach (var path in files)
            InsertImage(LoadImageFile(path));
    }

    private static IEnumerable<string> DroppedImages(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths.Where(p => ImageExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
            : Enumerable.Empty<string>();

    private void InsertImage(BitmapSource? source)
    {
        if (source == null || _activeNote == null) return;

        var image = new Image
        {
            Source = source,
            Width = Math.Min(source.PixelWidth, MaxImageWidth)
        };
        PrepareImage(image);

        var container = new InlineUIContainer(image, Editor.CaretPosition);
        Editor.CaretPosition = container.ElementEnd;
        Editor.Focus();

        _activeNote.HasImages = true;
        _activeNote.PlainText = ReadPlainText();
        MarkBodyDirty();

        UpdatePlaceholders();
    }

    /// <summary>
    /// Re-encodes to PNG so the bitmap owns its bytes. Clipboard images and file-backed ones can
    /// otherwise carry a source the document serialiser won't embed.
    /// </summary>
    private static BitmapImage? Stabilise(BitmapSource? source)
    {
        if (source == null) return null;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        buffer.Position = 0;

        return FromStream(buffer);
    }

    private BitmapImage? LoadImageFile(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            return FromStream(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ShowError($"Couldn't add {Path.GetFileName(path)} — {ex.Message}");
            return null;
        }
    }

    private static BitmapImage FromStream(Stream stream)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;   // read it all now so nothing stays locked
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    // ===== Prompt sets =====
    //
    // A prompt note's body is a queue rather than a document: prompts waiting to go, then prompts
    // already sent. The list carries its own order — position in the JSON array is position on
    // screen — so sending a prompt is a move to the end of the list and taking one back is a move
    // to the end of the unsent block. There is no order field to keep in step with anything.

    private static readonly TimeSpan PromptUndoWindow = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan CopiedFlash = TimeSpan.FromMilliseconds(1200);

    private const string CopyGlyph = "M4.5,4.5 H12.5 V12.5 H4.5 Z M9.5,4.5 V1.5 H1.5 V9.5 H4.5";
    private const string CopiedGlyph = "M2,7.5 L5.5,11 L12,3.5";
    private const string PromptBinGlyph = "M1.5,3.5 H12.5 M5,3.5 V1.5 H9 V3.5 M3,3.5 V12.5 H11 V3.5 M5.8,6 V10 M8.2,6 V10";
    private const string GripGlyph = "M1.5,1 V11 M5.5,1 V11";

    private readonly List<Prompt> _prompts = new();
    private readonly DispatcherTimer _promptUndoTimer;

    private bool _promptsDirty;

    /// <summary>Set when the prompt file exists but could not be read — never write over it.</summary>
    private bool _promptsReadOnly;

    /// <summary>The prompt whose card should take focus and animate in once the rebuild lands.</summary>
    private Prompt? _focusPrompt;

    private Prompt? _undoPrompt;
    private int _undoIndex;

    private Point _promptDragStart;
    private Prompt? _promptDragItem;
    private bool _promptDragging;

    /// <summary>
    /// Puts the editor into document mode or prompt mode. A prompt note has nothing to format and
    /// no body file, so the formatting bar folds away and takes its toggle with it.
    /// </summary>
    private void SyncEditorMode()
    {
        var prompt = _activeNote?.IsPrompt == true;

        PromptSurface.Visibility = prompt ? Visibility.Visible : Visibility.Collapsed;
        Editor.Visibility = prompt ? Visibility.Collapsed : Visibility.Visible;
        FormatToggle.Visibility = prompt ? Visibility.Collapsed : Visibility.Visible;
        TitlePlaceholder.Text = prompt ? "Application name" : "Title";

        // Folding the bar to zero height leaves its buttons in the tab order and the automation
        // tree, where they would target a document this note does not have. Collapse it outright.
        FormatBarHost.Visibility = prompt ? Visibility.Collapsed : Visibility.Visible;

        RestoreFormatBar();

        if (prompt)
        {
            LoadPromptSet(_activeNote!);
            return;
        }

        _prompts.Clear();
        _promptsReadOnly = false;
        _promptsDirty = false;
        PromptStack.Children.Clear();
    }

    private void LoadPromptSet(Note note)
    {
        var loaded = _store.LoadPrompts(note.Id);
        _promptsReadOnly = loaded == null;

        _prompts.Clear();
        if (loaded != null) _prompts.AddRange(loaded);

        NormalisePrompts();
        _promptsDirty = false;

        RebuildPrompts();
        ReportStoreError();
    }

    /// <summary>
    /// Sent prompts always follow the unsent ones, oldest sent first. The file is the order, so an
    /// older or hand-edited one gets straightened out on the way in rather than at every read.
    /// </summary>
    private void NormalisePrompts()
    {
        var ordered = _prompts.Where(p => !p.Sent)
            .Concat(_prompts.Where(p => p.Sent).OrderBy(p => p.SentAt ?? DateTime.MinValue))
            .ToList();

        _prompts.Clear();
        _prompts.AddRange(ordered);
    }

    /// <summary>
    /// The only thing that puts a prompt on screen. Structural changes only — typing writes straight
    /// through to the model, because rebuilding mid-keystroke would take the caret with it.
    /// </summary>
    private void RebuildPrompts()
    {
        PromptStack.Children.Clear();
        if (_activeNote is not { IsPrompt: true }) return;

        var unsent = _prompts.Where(p => !p.Sent).ToList();
        var sent = _prompts.Where(p => p.Sent).ToList();

        // The row reads these, so they settle here rather than waiting for the debounce — and the
        // mirror settles with them, or a deleted prompt would still turn up in search until the
        // next keystroke. Skipped for a set we could not read, whose queue is empty by accident.
        if (!_promptsReadOnly)
        {
            _activeNote.PromptTotal = _prompts.Count;
            _activeNote.PromptSent = sent.Count;
            _activeNote.PlainText = ReadPromptText();
        }

        PromptStack.Children.Add(BuildPromptHeader($"PROMPTS TO SEND ({unsent.Count})", first: true));

        for (var i = 0; i < unsent.Count; i++)
            PromptStack.Children.Add(BuildPromptCard(unsent[i], i + 1));

        if (unsent.Count == 0)
            PromptStack.Children.Add(BuildPromptNote(sent.Count > 0 ? "Nothing left to send." : "No prompts yet."));

        if (!_showingBin && !_promptsReadOnly)
            PromptStack.Children.Add(BuildAddPromptRow());

        if (sent.Count == 0) return;

        PromptStack.Children.Add(BuildPromptHeader($"SENT ({sent.Count})", first: false));

        for (var i = 0; i < sent.Count; i++)
            PromptStack.Children.Add(BuildPromptCard(sent[i], i + 1));
    }

    private TextBlock BuildPromptHeader(string text, bool first) => new()
    {
        Text = text,
        FontFamily = UiFont,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = (Brush)FindResource("SecondaryText"),
        Margin = new Thickness(4, first ? 8 : 18, 0, 8)
    };

    private TextBlock BuildPromptNote(string text) => new()
    {
        Text = text,
        FontFamily = UiFont,
        FontSize = 12,
        Foreground = (Brush)FindResource("SecondaryText"),
        Margin = new Thickness(4, 2, 0, 6)
    };

    // ===== One prompt card =====

    private Border BuildPromptCard(Prompt prompt, int serial)
    {
        var editable = !_showingBin && !_promptsReadOnly;

        // grip · tick · number · text · copy · delete
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var grip = BuildPromptGrip();
        // Hidden rather than collapsed: the text has to start at the same place in both sections.
        grip.Visibility = !prompt.Sent && editable ? Visibility.Visible : Visibility.Hidden;

        var tick = BuildPromptTick(prompt, editable);

        var number = new TextBlock
        {
            Text = $"{serial}.",
            FontFamily = UiFont,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("SecondaryText"),
            MinWidth = 20,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(2, 5, 8, 0),
            VerticalAlignment = VerticalAlignment.Top
        };

        var body = BuildPromptBody(prompt, editable);

        var copyGlyph = new ShapePath
        {
            Data = Geometry.Parse(CopyGlyph),
            Stroke = (Brush)FindResource("PowerIconStroke"),
            StrokeThickness = 1.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };

        var copy = new Button
        {
            Style = (Style)FindResource("PromptCardButtonStyle"),
            Content = copyGlyph,
            ToolTip = "Copy to clipboard"
        };
        copy.Click += (_, _) => CopyPrompt(prompt, copyGlyph);

        var delete = new Button
        {
            Style = (Style)FindResource("PromptCardButtonStyle"),
            ToolTip = "Delete prompt",
            Visibility = editable ? Visibility.Visible : Visibility.Collapsed,
            Content = new ShapePath
            {
                Data = Geometry.Parse(PromptBinGlyph),
                Stroke = (Brush)FindResource("PowerIconStroke"),
                StrokeThickness = 1.2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            }
        };
        delete.Click += (_, _) => DeletePrompt(prompt);

        Grid.SetColumn(grip, 0);
        Grid.SetColumn(tick, 1);
        Grid.SetColumn(number, 2);
        Grid.SetColumn(body.Host, 3);
        Grid.SetColumn(copy, 4);
        Grid.SetColumn(delete, 5);

        grid.Children.Add(grip);
        grid.Children.Add(tick);
        grid.Children.Add(number);
        grid.Children.Add(body.Host);
        grid.Children.Add(copy);
        grid.Children.Add(delete);

        var card = new Border
        {
            Background = (Brush)FindResource("SurfaceColor"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 8, 8, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Tag = prompt,
            Child = grid
        };

        if (!prompt.Sent && editable) AttachPromptDrag(card, grip, prompt);

        // Only the card you just added arrives with motion; rebuilding the whole queue on every
        // tick would otherwise flicker the lot.
        if (ReferenceEquals(prompt, _focusPrompt))
        {
            _focusPrompt = null;
            AnimatePromptCardIn(card);

            Dispatcher.BeginInvoke(() =>
            {
                body.Box.Focus();
                card.BringIntoView();
            }, DispatcherPriority.Loaded);
        }

        return card;
    }

    /// <summary>The editable prompt text, its watermark, and the stamp a sent prompt carries.</summary>
    private (Panel Host, TextBox Box) BuildPromptBody(Prompt prompt, bool editable)
    {
        var placeholder = new TextBlock
        {
            Text = "Type or paste a prompt…",
            FontFamily = UiFont,
            FontSize = 13,
            Foreground = (Brush)FindResource("SecondaryText"),
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Visibility = prompt.IsEmpty ? Visibility.Visible : Visibility.Collapsed
        };

        var box = new TextBox
        {
            Text = prompt.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = UiFont,
            FontSize = 13,
            // A sent prompt is a record, so it recedes — but it stays readable and copyable.
            Foreground = (Brush)FindResource(prompt.Sent ? "SecondaryText" : "PrimaryText"),
            CaretBrush = (Brush)FindResource("PrimaryText"),
            SelectionBrush = (Brush)FindResource("NotesAccent"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            IsReadOnly = !editable,
            VerticalAlignment = VerticalAlignment.Center
        };

        box.TextChanged += (_, _) =>
        {
            placeholder.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (_activeNote is not { IsPrompt: true }) return;

            // Straight through, so the row's preview tracks your typing the way a note's does.
            prompt.Text = box.Text;
            _activeNote.PlainText = ReadPromptText();
            MarkPromptsDirty();
        };

        var text = new Grid { Children = { placeholder, box } };

        var host = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 6, 1),
            Children = { text }
        };

        if (prompt is { Sent: true, SentAt: not null })
            host.Children.Add(new TextBlock
            {
                Text = Prompt.FormatSentAt(prompt.SentAt.Value, DateTime.Now),
                FontFamily = UiFont,
                FontSize = 10.5,
                Foreground = (Brush)FindResource("SecondaryText"),
                Opacity = 0.85,
                Margin = new Thickness(0, 5, 0, 0)
            });

        return (host, box);
    }

    /// <summary>The tick that moves a prompt between the two sections. Amber, because Notes is amber.</summary>
    private Border BuildPromptTick(Prompt prompt, bool editable)
    {
        var accent = (Brush)FindResource("NotesAccent");

        var ring = new Ellipse
        {
            Width = 18,
            Height = 18,
            Stroke = prompt.Sent ? accent : (Brush)FindResource("SecondaryText"),
            StrokeThickness = 1.4,
            Fill = prompt.Sent ? accent : Brushes.Transparent
        };

        var check = new ShapePath
        {
            Data = Geometry.Parse("M1,4.5 L3.9,7.4 L9.5,1.6"),
            Stroke = (Brush)FindResource("NotesSelectionText"),
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 11,
            Height = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = prompt.Sent ? Visibility.Visible : Visibility.Collapsed
        };

        var tick = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(3),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
            Cursor = editable ? Cursors.Hand : Cursors.Arrow,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            ToolTip = prompt.Sent ? "Put back in the queue" : "Mark as sent",
            Child = new Grid { Width = 18, Height = 18, Children = { ring, check } }
        };

        if (!editable) return tick;

        tick.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;

            var scale = (ScaleTransform)tick.RenderTransform;
            var pop = new DoubleAnimation(0.8, 1, new Duration(TimeSpan.FromMilliseconds(260))) { EasingFunction = OutEase };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop.Clone());
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

            SetPromptSent(prompt, !prompt.Sent);
        };

        return tick;
    }

    /// <summary>
    /// The drag handle. A bare Path is hit-tested against its stroke, which would make a 1.3px line
    /// the whole target — so it sits inside a transparent Border that is grabbable across its area.
    /// </summary>
    private Border BuildPromptGrip() => new()
    {
        Background = Brushes.Transparent,
        Padding = new Thickness(4, 6, 5, 6),
        VerticalAlignment = VerticalAlignment.Top,
        Cursor = Cursors.SizeNS,
        ToolTip = "Drag to reorder",
        Child = new ShapePath
        {
            Data = Geometry.Parse(GripGlyph),
            Stroke = (Brush)FindResource("SecondaryText"),
            StrokeThickness = 1.3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = 0.45,
            Width = 7,
            Height = 12
        }
    };

    private Button BuildAddPromptRow()
    {
        // Its text starts at 9px in, which lines the "+" up with the grips on the cards above it.
        var button = new Button
        {
            Style = (Style)FindResource("PromptQuietButtonStyle"),
            Content = "+   Add prompt",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 0)
        };

        button.Click += (_, _) => AddPrompt(focus: true);
        return button;
    }

    // ===== Prompt commands =====

    private void AddPrompt(bool focus)
    {
        if (_activeNote is not { IsPrompt: true } || _showingBin || _promptsReadOnly) return;

        var prompt = new Prompt();

        // Onto the end of the queue, which is where the sent ones start.
        _prompts.Insert(_prompts.Count(p => !p.Sent), prompt);

        if (focus) _focusPrompt = prompt;

        MarkPromptsDirty();
        RebuildPrompts();
    }

    private void SetPromptSent(Prompt prompt, bool sent)
    {
        if (_showingBin || _promptsReadOnly) return;

        prompt.Sent = sent;
        prompt.SentAt = sent ? DateTime.Now : null;

        _prompts.Remove(prompt);

        // Sending is a move to the very end, so Sent reads oldest first; taking one back drops it
        // at the bottom of the queue rather than jumping it to the front.
        if (sent) _prompts.Add(prompt);
        else _prompts.Insert(_prompts.Count(p => !p.Sent), prompt);

        MarkPromptsDirty();
        RebuildPrompts();
    }

    private void CopyPrompt(Prompt prompt, ShapePath glyph)
    {
        if (prompt.IsEmpty) return;

        try
        {
            Clipboard.SetText(prompt.Text);
        }
        catch (ExternalException ex)
        {
            // Another application can hold the clipboard open — say so rather than doing nothing.
            ShowError($"Couldn't copy to the clipboard — {ex.Message}");
            return;
        }

        FlashCopied(glyph);
    }

    /// <summary>Turns the copy icon into a tick for a moment, so the click has an answer.</summary>
    private void FlashCopied(ShapePath glyph)
    {
        glyph.Data = Geometry.Parse(CopiedGlyph);
        glyph.Stroke = (Brush)FindResource("NotesAccent");

        var revert = new DispatcherTimer { Interval = CopiedFlash };
        revert.Tick += (_, _) =>
        {
            revert.Stop();
            glyph.Data = Geometry.Parse(CopyGlyph);
            glyph.Stroke = (Brush)FindResource("PowerIconStroke");
        };
        revert.Start();
    }

    private void DeletePrompt(Prompt prompt)
    {
        var index = _prompts.IndexOf(prompt);
        if (index < 0) return;

        // One deletion is undoable at a time; a second one simply forgets the first.
        _undoPrompt = prompt;
        _undoIndex = index;

        _prompts.RemoveAt(index);

        MarkPromptsDirty();
        RebuildPrompts();
        ShowPromptUndo();
    }

    /// <summary>
    /// A prompt card you opened and never typed into is a draft, not content — it goes on the way
    /// out, the same way a blank note evaporates.
    /// </summary>
    private void PruneBlankPrompts()
    {
        // Nothing in the bin is editable, so nothing there is a draft to tidy away either.
        if (_activeNote is not { IsPrompt: true } || _promptsReadOnly || _showingBin) return;

        if (_prompts.RemoveAll(p => !p.Sent && p.IsEmpty) > 0)
            _promptsDirty = true;
    }

    private string ReadPromptText() =>
        string.Join("\n", _prompts.Where(p => !p.IsEmpty).Select(p => p.Text.Trim()));

    private void MarkPromptsDirty()
    {
        if (_activeNote is not { IsPrompt: true } || _promptsReadOnly) return;

        _promptsDirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void WritePrompts(Note note)
    {
        if (!_promptsDirty) return;
        _promptsDirty = false;

        // Unreadable: writing now would destroy whatever is in the file. In the bin: the note is a
        // record to restore, and nothing may rewrite a record.
        if (_promptsReadOnly || _showingBin) return;

        note.PromptTotal = _prompts.Count;
        note.PromptSent = _prompts.Count(p => p.Sent);

        _store.SavePrompts(note.Id, _prompts);
        ReportStoreError();
    }

    // ===== Undo =====

    private void ShowPromptUndo()
    {
        PromptUndoToast.Visibility = Visibility.Visible;
        PromptUndoToast.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(180))) { EasingFunction = SoftEase });

        _promptUndoTimer.Stop();
        _promptUndoTimer.Start();
    }

    private void PromptUndo_Click(object sender, RoutedEventArgs e)
    {
        _promptUndoTimer.Stop();

        var restored = _undoPrompt;
        _undoPrompt = null;

        if (restored != null && _activeNote is { IsPrompt: true })
        {
            _prompts.Insert(Math.Min(_undoIndex, _prompts.Count), restored);
            NormalisePrompts();
            MarkPromptsDirty();
            RebuildPrompts();
        }

        HidePromptUndo();
    }

    private void DismissPromptUndo()
    {
        _promptUndoTimer.Stop();

        if (_undoPrompt == null && PromptUndoToast.Visibility != Visibility.Visible) return;

        _undoPrompt = null;
        HidePromptUndo();
    }

    private void HidePromptUndo()
    {
        var fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(180))) { EasingFunction = SoftEase };

        // Re-check: a fast delete-after-undo must not collapse a freshly shown toast.
        fade.Completed += (_, _) =>
        {
            if (_undoPrompt == null) PromptUndoToast.Visibility = Visibility.Collapsed;
        };

        PromptUndoToast.BeginAnimation(OpacityProperty, fade);
    }

    // ===== Reordering =====
    //
    // The grip is the handle, not the whole card: the card's middle is an editable text box, and
    // dragging there has to mean selecting text.

    private void AttachPromptDrag(Border card, UIElement grip, Prompt prompt)
    {
        grip.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _promptDragStart = e.GetPosition(PromptScroll);
            _promptDragItem = prompt;
            _promptDragging = false;
            e.Handled = true;
        };

        grip.PreviewMouseMove += (_, e) =>
        {
            if (_promptDragItem == null || e.LeftButton != MouseButtonState.Pressed) return;
            if (!ReferenceEquals(_promptDragItem, prompt)) return;

            var now = e.GetPosition(PromptScroll);

            if (!_promptDragging)
            {
                if (Math.Abs(now.Y - _promptDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

                _promptDragging = true;
                grip.CaptureMouse();

                // The arrival fade may still be running on this very card.
                card.BeginAnimation(OpacityProperty, null);
                card.Opacity = 0.75;
                Panel.SetZIndex(card, 5);
            }

            MovePromptDuringDrag(card, now);
            e.Handled = true;
        };

        grip.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (grip.IsMouseCaptured) grip.ReleaseMouseCapture();

            if (_promptDragging)
            {
                _promptDragging = false;
                card.Opacity = 1;
                Panel.SetZIndex(card, 0);
                CommitPromptOrder();
                e.Handled = true;

                // Releasing capture mid-route and rebuilding the tree in the same handler is the
                // trap CloseDuePopup documents — renumber once the event has finished delivering.
                Dispatcher.BeginInvoke(RebuildPrompts, DispatcherPriority.Input);
            }

            _promptDragItem = null;
        };
    }

    /// <summary>Moves the dragged card within the stack as the pointer crosses its neighbours.</summary>
    private void MovePromptDuringDrag(Border card, Point pointer)
    {
        var siblings = PromptStack.Children.OfType<Border>()
            .Where(b => b.Tag is Prompt { Sent: false })
            .ToList();

        var index = siblings.IndexOf(card);
        if (index < 0) return;

        for (var i = 0; i < siblings.Count; i++)
        {
            if (i == index) continue;

            var other = siblings[i];
            var mid = other.TranslatePoint(new Point(0, 0), PromptScroll).Y + other.ActualHeight / 2;

            var crossed = i < index ? pointer.Y < mid : pointer.Y > mid;
            if (!crossed) continue;

            PromptStack.Children.Remove(card);
            PromptStack.Children.Insert(PromptStack.Children.IndexOf(other) + (i < index ? 0 : 1), card);
            return;
        }
    }

    /// <summary>Takes the order back off the screen, which is where the drag actually happened.</summary>
    private void CommitPromptOrder()
    {
        var ordered = PromptStack.Children.OfType<Border>()
            .Select(b => b.Tag)
            .OfType<Prompt>()
            .Where(p => !p.Sent)
            .ToList();

        if (ordered.Count == 0) return;

        var sent = _prompts.Where(p => p.Sent).ToList();

        _prompts.Clear();
        _prompts.AddRange(ordered);
        _prompts.AddRange(sent);

        MarkPromptsDirty();
    }

    private void AnimatePromptCardIn(Border card)
    {
        var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220))) { EasingFunction = SoftEase };

        // Hand the value back, or a held animation on Opacity outranks the dim a drag applies.
        fade.Completed += (_, _) =>
        {
            card.BeginAnimation(OpacityProperty, null);
            card.Opacity = 1;
        };

        card.BeginAnimation(OpacityProperty, fade);

        var rise = new TranslateTransform();
        card.RenderTransform = rise;
        rise.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, new Duration(TimeSpan.FromMilliseconds(260))) { EasingFunction = OutEase });
    }

    // ===== View state =====

    private void UpdateEditorHeader() =>
        EditorDate.Text = _activeNote?.EditedOnLabel ?? "";

    private void UpdateEmptyState()
    {
        var hasNote = _activeNote != null;
        var wasEmpty = EmptyState.Visibility == Visibility.Visible;

        EmptyState.Visibility = hasNote ? Visibility.Collapsed : Visibility.Visible;
        EditorSurface.Visibility = hasNote ? Visibility.Visible : Visibility.Collapsed;

        var listEmpty = _view.View.IsEmpty;
        EmptyStateTitle.Text = _showingBin
            ? (listEmpty ? "Nothing Deleted" : "No Note Selected")
            : (listEmpty ? "No Notes" : "No Note Selected");

        // Nothing to create in the bin, and nothing to create from if a search is hiding everything.
        EmptyStateButton.Visibility = _showingBin ? Visibility.Collapsed : Visibility.Visible;

        if (!hasNote && !wasEmpty) AnimateEmptyStateIn();
    }

    private void UpdatePlaceholders()
    {
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        TitlePlaceholder.Visibility = _activeNote != null && TitleBox.Text.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        // A prompt note's cards carry their own placeholder; the document one would sit behind them.
        var bodyEmpty = _activeNote is { IsPrompt: false }
                        && ReadPlainText().Length == 0
                        && !DocumentImages(Editor.Document).Any();

        EditorPlaceholder.Visibility = bodyEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateNoResults()
    {
        var empty = _view.View.IsEmpty;
        NoResultsText.Visibility = empty && SearchBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateNoteCommands()
    {
        var note = NotesList.SelectedItem as Note;

        PinButton.IsEnabled = note != null;
        DeleteButton.IsEnabled = note != null;
        RestoreButton.IsEnabled = note != null;

        var pinned = note?.Pinned == true;
        PinButtonGlyph.Fill = pinned ? (Brush)FindResource("NotesAccent") : Brushes.Transparent;
        PinButtonGlyph.Stroke = (Brush)FindResource(pinned ? "NotesAccent" : "PowerIconStroke");
        MenuPin.Header = pinned ? "Unpin Note" : "Pin Note";
    }

    private void ReportStoreError()
    {
        var error = _store.LastError;

        if (error == null)
        {
            ErrorStrip.Visibility = Visibility.Collapsed;
            return;
        }

        ErrorText.Text = error;
        if (ErrorStrip.Visibility == Visibility.Visible) return;

        ErrorStrip.Visibility = Visibility.Visible;
        ErrorStrip.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220))) { EasingFunction = SoftEase });
        ErrorSlide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(24, 0, new Duration(TimeSpan.FromMilliseconds(300))) { EasingFunction = OutEase });
    }

    // ===== Motion =====

    /// <summary>Cross-fades the editor when the selected note changes.</summary>
    private void AnimateEditorIn()
    {
        EditorSurface.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200))) { EasingFunction = SoftEase });

        // Kept very shallow: the editor holds live text, and a heavier scale would blur it mid-typing.
        var grow = new DoubleAnimation(0.99, 1, new Duration(TimeSpan.FromMilliseconds(240))) { EasingFunction = OutEase };
        EditorScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow.Clone());
        EditorScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    private void AnimateEmptyStateIn() =>
        EmptyState.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(260))) { EasingFunction = SoftEase });

    private void SmoothScroll_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var view = FindScrollViewer((DependencyObject)sender);
        if (view == null || view.ScrollableHeight <= 0) return;

        e.Handled = true;

        if (!_scrollers.TryGetValue(view, out var scroller))
        {
            scroller = new SmoothScroller(view);
            _scrollers[view] = scroller;
        }

        scroller.Nudge(e.Delta);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer found) return found;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var result = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (result != null) return result;
        }

        return null;
    }

    // ===== Divider and selection persistence =====

    private void Splitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsPath);
        key.SetValue(ListWidthKey, (int)ListColumn.ActualWidth, RegistryValueKind.DWord);
    }

    private void RestoreListWidth()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsPath, false);
        if (key?.GetValue(ListWidthKey) is int stored && stored >= MinListWidth && stored <= MaxListWidth)
            ListColumn.Width = new GridLength(stored);
    }

    private void SelectRestoredNote()
    {
        string? wanted;
        using (var key = Registry.CurrentUser.OpenSubKey(SettingsPath, false))
            wanted = key?.GetValue(SelectedIdKey) as string;

        var note = string.IsNullOrEmpty(wanted)
            ? null
            : _store.Notes.FirstOrDefault(n => n.Id == wanted && !n.IsDeleted);

        _suppressSelectionChange = true;
        NotesList.SelectedItem = note;
        _suppressSelectionChange = false;

        _activeNote = note;
        LoadActiveNote();

        if (note == null)
            SelectFirstAvailable();
        else
            NotesList.ScrollIntoView(note);
    }
}
