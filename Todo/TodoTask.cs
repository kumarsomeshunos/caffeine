using System;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace CaffeineWin.Todo;

/// <summary>How a task repeats once it is completed. Presets only — no custom rules.</summary>
public enum Recurrence { None, Daily, Weekly, Monthly, Yearly }

/// <summary>How the task list is ordered on screen.</summary>
public enum TaskSort { Manual, Date, Title }

/// <summary>Row height for the task list.</summary>
public enum TaskDensity { Comfortable, Compact }

/// <summary>
/// A single task. Subtasks are ordinary tasks with <see cref="ParentId"/> set — one level only,
/// as in Google Tasks.
/// </summary>
public sealed class TodoTask : INotifyPropertyChanged
{
    private string _title = "";
    private string _notes = "";
    private DateTime? _due;
    private bool _hasTime;
    private Recurrence _repeat;
    private bool _completed;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ListId { get; set; } = "";

    /// <summary>Set when this task is a subtask of another. Only one level deep is allowed.</summary>
    public string? ParentId { get; set; }

    public int Order { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }

    /// <summary>Set once a due balloon has been shown, so it fires exactly once per due date.</summary>
    public bool Notified { get; set; }

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value ?? "";
            Raise(nameof(Title));
            Raise(nameof(DisplayTitle));
            Raise(nameof(IsEmpty));
        }
    }

    public string Notes
    {
        get => _notes;
        set
        {
            if (_notes == value) return;
            _notes = value ?? "";
            Raise(nameof(Notes));
            Raise(nameof(HasNotes));
        }
    }

    /// <summary>Due date, with a meaningful time component only when <see cref="HasTime"/> is set.</summary>
    public DateTime? Due
    {
        get => _due;
        set
        {
            if (_due == value) return;
            _due = value;
            Notified = false;
            RaiseDueLabels();
        }
    }

    public bool HasTime
    {
        get => _hasTime;
        set
        {
            if (_hasTime == value) return;
            _hasTime = value;
            Notified = false;
            RaiseDueLabels();
        }
    }

    public Recurrence Repeat
    {
        get => _repeat;
        set
        {
            if (_repeat == value) return;
            _repeat = value;
            Raise(nameof(Repeat));
            Raise(nameof(RepeatLabel));
            Raise(nameof(Repeats));
        }
    }

    public bool Completed
    {
        get => _completed;
        set
        {
            if (_completed == value) return;
            _completed = value;
            CompletedAt = value ? DateTime.Now : null;
            Raise(nameof(Completed));
        }
    }

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "New task" : Title.Trim();

    [JsonIgnore] public bool IsEmpty => string.IsNullOrWhiteSpace(Title);
    [JsonIgnore] public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    [JsonIgnore] public bool IsSubtask => !string.IsNullOrEmpty(ParentId);
    [JsonIgnore] public bool Repeats => Repeat != Recurrence.None;
    [JsonIgnore] public bool HasDue => _due != null;

    /// <summary>The moment this task actually falls due, used for both sorting and notifying.</summary>
    [JsonIgnore]
    public DateTime? DueAt => _due == null
        ? null
        : _hasTime ? _due : _due.Value.Date.AddHours(TodoSettings.DefaultDueHour)
                                     .AddMinutes(TodoSettings.DefaultDueMinute);

    [JsonIgnore] public bool IsOverdue => !Completed && DueAt != null && DueAt < DateTime.Now;

    [JsonIgnore]
    public bool IsDueToday => _due != null && _due.Value.Date == DateTime.Today;

    [JsonIgnore] public string DueLabel => FormatDue(_due, _hasTime, DateTime.Now);

    [JsonIgnore]
    public string RepeatLabel => Repeat switch
    {
        Recurrence.Daily => "Every day",
        Recurrence.Weekly => _due == null ? "Every week" : $"Every {_due.Value:dddd}",
        Recurrence.Monthly => _due == null ? "Every month" : $"Monthly on the {_due.Value.Day}",
        Recurrence.Yearly => _due == null ? "Every year" : $"Yearly on {_due.Value:d MMM}",
        _ => "Never"
    };

    // ===== Pure helpers =====

    /// <summary>Relative, human due text: Today, Tomorrow, a weekday, then a date — plus the time.</summary>
    public static string FormatDue(DateTime? due, bool hasTime, DateTime now)
    {
        if (due == null) return "";

        var days = due.Value.Date.Subtract(now.Date).Days;
        var day = days switch
        {
            0 => "Today",
            1 => "Tomorrow",
            -1 => "Yesterday",
            > 1 and < 7 => due.Value.ToString("dddd"),
            _ => due.Value.ToString("d MMM")
        };

        return hasTime ? $"{day} · {due.Value:HH:mm}" : day;
    }

    /// <summary>The next occurrence after this one, for a repeating task that has just been completed.</summary>
    public static DateTime NextOccurrence(DateTime from, Recurrence repeat) => repeat switch
    {
        Recurrence.Daily => from.AddDays(1),
        Recurrence.Weekly => from.AddDays(7),
        Recurrence.Monthly => from.AddMonths(1),
        Recurrence.Yearly => from.AddYears(1),
        _ => from
    };

    private void RaiseDueLabels()
    {
        Raise(nameof(Due));
        Raise(nameof(HasTime));
        Raise(nameof(DueAt));
        Raise(nameof(HasDue));
        Raise(nameof(DueLabel));
        Raise(nameof(IsOverdue));
        Raise(nameof(IsDueToday));
        Raise(nameof(RepeatLabel));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
