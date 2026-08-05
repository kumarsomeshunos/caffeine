using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;

namespace CaffeineWin.Notes;

/// <summary>What a note's body actually is: a formatted document, or a queue of prompts.</summary>
public enum NoteKind { Text, Prompt }

/// <summary>
/// One note's index entry. The formatted body lives in its own file (see <see cref="NotesStore"/>);
/// what is kept here is everything the list needs to render and search without opening a document —
/// the authored title and a plain-text mirror of the body.
/// </summary>
public sealed class Note : INotifyPropertyChanged
{
    public const string UntitledLabel = "New Note";
    public const string UntitledPromptLabel = "New Prompt Set";
    public const string NoPreviewLabel = "No additional text";

    /// <summary>How long a deleted note is kept before it is purged on load.</summary>
    public const int RetentionDays = 30;

    private string _title = "";
    private string _plainText = "";
    private DateTime _modifiedAt = DateTime.Now;
    private bool _pinned;
    private DateTime? _deletedAt;
    private NoteKind _kind;
    private int _promptTotal;
    private int _promptSent;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Authored, not scraped from the first line — the title is its own field in the editor.</summary>
    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value ?? "";
            Raise(nameof(Title));
            Raise(nameof(DisplayTitle));
            Raise(nameof(AccessibleLabel));
            Raise(nameof(IsEmpty));
        }
    }

    /// <summary>
    /// Plain-text mirror of the formatted body, refreshed whenever the body is saved. Search and the
    /// row previews read this so neither has to load a document.
    /// </summary>
    public string PlainText
    {
        get => _plainText;
        set
        {
            if (_plainText == value) return;
            _plainText = value ?? "";
            Raise(nameof(PlainText));
            Raise(nameof(Preview));
            Raise(nameof(IsEmpty));
        }
    }

    public DateTime ModifiedAt
    {
        get => _modifiedAt;
        set
        {
            if (_modifiedAt == value) return;
            _modifiedAt = value;
            Raise(nameof(ModifiedAt));
            Raise(nameof(TimeLabel));
            Raise(nameof(EditedOnLabel));
        }
    }

    public bool Pinned
    {
        get => _pinned;
        set
        {
            if (_pinned == value) return;
            _pinned = value;
            Raise(nameof(Pinned));
            Raise(nameof(GroupKey));
        }
    }

    /// <summary>When the note was moved to Recently Deleted, or null while it is a live note.</summary>
    public DateTime? DeletedAt
    {
        get => _deletedAt;
        set
        {
            if (_deletedAt == value) return;
            _deletedAt = value;
            Raise(nameof(DeletedAt));
            Raise(nameof(IsDeleted));
            Raise(nameof(TimeLabel));
        }
    }

    /// <summary>
    /// Text note or prompt set. Written by versions that only had one kind of note, so an index
    /// entry without it reads back as <see cref="NoteKind.Text"/> — which is what it was.
    /// </summary>
    public NoteKind Kind
    {
        get => _kind;
        set
        {
            if (_kind == value) return;
            _kind = value;
            Raise(nameof(Kind));
            Raise(nameof(IsPrompt));
            Raise(nameof(DisplayTitle));
            Raise(nameof(AccessibleLabel));
        }
    }

    /// <summary>
    /// How many prompts the set holds and how many have gone. Mirrored into the index for the same
    /// reason <see cref="PlainText"/> is: the list row must render without opening the prompt file.
    /// </summary>
    public int PromptTotal
    {
        get => _promptTotal;
        set
        {
            if (_promptTotal == value) return;
            _promptTotal = value;
            Raise(nameof(PromptTotal));
            Raise(nameof(PromptCountLabel));
            Raise(nameof(AccessibleLabel));
        }
    }

    public int PromptSent
    {
        get => _promptSent;
        set
        {
            if (_promptSent == value) return;
            _promptSent = value;
            Raise(nameof(PromptSent));
            Raise(nameof(PromptCountLabel));
            Raise(nameof(AccessibleLabel));
        }
    }

    /// <summary>
    /// Set when the body contains pictures. Without it a note holding nothing but an image would
    /// look blank to <see cref="IsEmpty"/> and get discarded on the way out.
    /// </summary>
    public bool HasImages { get; set; }

    /// <summary>
    /// The plain-text body written by versions before rich text. Present only until
    /// <see cref="NotesStore.Load"/> folds it into <see cref="Title"/> and <see cref="PlainText"/>.
    /// </summary>
    public string? Body { get; set; }

    /// <summary>What the list shows: the title, or a placeholder while it is still blank.</summary>
    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
        ? (IsPrompt ? UntitledPromptLabel : UntitledLabel)
        : Title.Trim();

    [JsonIgnore] public string Preview => DerivePreview(PlainText);
    [JsonIgnore] public string EditedOnLabel => FormatEditorTimestamp(ModifiedAt);
    [JsonIgnore] public bool IsDeleted => _deletedAt != null;
    [JsonIgnore] public bool IsPrompt => _kind == NoteKind.Prompt;

    /// <summary>Progress through the queue, shown on the row beside the timestamp.</summary>
    [JsonIgnore] public string PromptCountLabel => $"{_promptSent}/{_promptTotal} sent";

    /// <summary>
    /// What a screen reader announces for the row. The row's template has no ContentPresenter, so
    /// nothing inside it reaches the automation tree — the P badge and the count included, which is
    /// why they are spelled out here rather than left to be read off the row.
    /// </summary>
    [JsonIgnore]
    public string AccessibleLabel => IsPrompt
        ? $"{DisplayTitle}, prompt set, {_promptSent} of {_promptTotal} sent"
        : DisplayTitle;

    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(PlainText) && !HasImages;

    /// <summary>Whole days left before purging. Zero means it goes on the next load.</summary>
    [JsonIgnore]
    public int DaysLeft => _deletedAt == null
        ? RetentionDays
        : Math.Max(0, RetentionDays - (int)(DateTime.Now.Date - _deletedAt.Value.Date).TotalDays);

    /// <summary>
    /// In the bin a note's edit time matters less than how long is left to rescue it, so the row
    /// shows the countdown instead.
    /// </summary>
    [JsonIgnore]
    public string TimeLabel => IsDeleted
        ? DaysLeft switch { 0 => "Deleting today", 1 => "1 day left", _ => $"{DaysLeft} days left" }
        : FormatListTimestamp(ModifiedAt, DateTime.Now);

    /// <summary>
    /// Group header the list places this note under, already in display form. Pinned notes sort
    /// above the rest; headers are only shown at all when something is pinned.
    /// </summary>
    [JsonIgnore] public string GroupKey => Pinned ? "PINNED" : "NOTES";

    // ===== Derivation (pure — no UI, no state) =====

    /// <summary>
    /// The first two non-blank lines of the body, joined by a newline so the row can render them as a
    /// two-line block. Falls back to "No additional text".
    /// </summary>
    public static string DerivePreview(string? plainText)
    {
        var sb = new StringBuilder();
        var taken = 0;

        foreach (var line in NonBlankLines(plainText))
        {
            if (taken > 0) sb.Append('\n');
            sb.Append(line.Length > 200 ? line[..200] : line);

            if (++taken == 2) break;
        }

        return taken == 0 ? NoPreviewLabel : sb.ToString();
    }

    /// <summary>Splits a legacy plain-text body into an authored title and the remaining lines.</summary>
    public static (string Title, string Remainder) SplitLegacyBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return ("", "");

        var lines = body.Replace("\r\n", "\n").Split('\n');
        var titleIndex = Array.FindIndex(lines, l => l.Trim().Length > 0);
        if (titleIndex < 0) return ("", "");

        var title = lines[titleIndex].Trim();
        var remainder = string.Join("\n", lines[(titleIndex + 1)..]).TrimStart('\n');
        return (title, remainder);
    }

    /// <summary>Relative stamp for list rows: today's time, "Yesterday", a weekday, then a date.</summary>
    public static string FormatListTimestamp(DateTime modified, DateTime now)
    {
        var day = now.Date.Subtract(modified.Date).Days;

        return day switch
        {
            0 => $"Today {modified:HH:mm}",
            1 => "Yesterday",
            > 1 and < 7 => modified.ToString("dddd"),
            _ => modified.ToString("dd/MM/yyyy")
        };
    }

    /// <summary>Long-form stamp for the centred line above the editor.</summary>
    public static string FormatEditorTimestamp(DateTime modified) =>
        modified.ToString("d MMMM yyyy 'at' HH:mm");

    private static IEnumerable<string> NonBlankLines(string? body)
    {
        if (string.IsNullOrEmpty(body)) yield break;

        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length > 0) yield return line;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
