using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaffeineWin.Todo;

/// <summary>
/// Lists and tasks in a single <c>tasks.json</c> beside the notes. One file is enough here: unlike
/// notes there is no rich content, so the whole model is small even with a few thousand tasks.
/// <para>
/// <c>App</c> owns the one instance and loads it at startup — due reminders have to work whether or
/// not the Todo tab has ever been opened.
/// </para>
/// </summary>
public sealed class TodoStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Caffeine");

    public static string FilePath => Path.Combine(FolderPath, "tasks.json");

    public ObservableCollection<TaskList> Lists { get; } = new();
    public ObservableCollection<TodoTask> Tasks { get; } = new();

    /// <summary>Set when the last load or save failed. The view surfaces this.</summary>
    public string? LastError { get; private set; }

    private sealed class Snapshot
    {
        public List<TaskList> Lists { get; set; } = new();
        public List<TodoTask> Tasks { get; set; } = new();
    }

    public void Load()
    {
        LastError = null;
        Lists.Clear();
        Tasks.Clear();

        if (!File.Exists(FilePath))
        {
            SeedFirstList();
            return;
        }

        string json;
        try
        {
            json = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"Couldn't open tasks.json — {ex.Message}";
            SeedFirstList();
            return;
        }

        Snapshot? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<Snapshot>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            // Never let a save overwrite a file we failed to parse — set it aside first.
            var kept = Quarantine();
            LastError = kept == null
                ? $"tasks.json is unreadable — {ex.Message}"
                : $"tasks.json was unreadable and has been kept as {Path.GetFileName(kept)}";
            SeedFirstList();
            return;
        }

        if (loaded == null)
        {
            SeedFirstList();
            return;
        }

        foreach (var list in loaded.Lists.OrderBy(l => l.Order))
            Lists.Add(list);

        // A task whose list has gone is orphaned; drop it rather than leave it invisible for ever.
        var known = Lists.Select(l => l.Id).ToHashSet();
        foreach (var task in loaded.Tasks.Where(t => known.Contains(t.ListId)))
            Tasks.Add(task);

        if (Lists.Count == 0) SeedFirstList();
    }

    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);

            var snapshot = new Snapshot
            {
                Lists = new List<TaskList>(Lists),
                Tasks = new List<TodoTask>(Tasks)
            };

            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, SerializerOptions));
            File.Move(temp, FilePath, overwrite: true);

            LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"Couldn't save tasks — {ex.Message}";
            return false;
        }
    }

    // ===== Queries the view and the reminder check share =====

    /// <summary>Top-level tasks of a list, in the order the given sort implies.</summary>
    public IEnumerable<TodoTask> TopLevel(string listId, TaskSort sort, bool completed) =>
        Sorted(Tasks.Where(t => t.ListId == listId && !t.IsSubtask && t.Completed == completed), sort);

    /// <summary>Subtasks of a task, always in manual order.</summary>
    public IEnumerable<TodoTask> Children(string parentId, bool completed) =>
        Tasks.Where(t => t.ParentId == parentId && t.Completed == completed).OrderBy(t => t.Order);

    public int OutstandingCount(string listId) =>
        Tasks.Count(t => t.ListId == listId && !t.Completed);

    private static IEnumerable<TodoTask> Sorted(IEnumerable<TodoTask> tasks, TaskSort sort) => sort switch
    {
        // Undated tasks sink to the bottom rather than sorting as "no date".
        TaskSort.Date => tasks.OrderBy(t => t.DueAt ?? DateTime.MaxValue).ThenBy(t => t.Order),
        TaskSort.Title => tasks.OrderBy(t => t.DisplayTitle, StringComparer.CurrentCultureIgnoreCase),
        _ => tasks.OrderBy(t => t.Order)
    };

    /// <summary>Tasks that have come due and have not been announced yet.</summary>
    public IEnumerable<TodoTask> DueForReminder(DateTime now) =>
        Tasks.Where(t => !t.Completed && !t.Notified && t.DueAt != null && t.DueAt <= now);

    /// <summary>The next free order value at the end of a list.</summary>
    public int NextOrder(string listId, string? parentId) =>
        Tasks.Where(t => t.ListId == listId && t.ParentId == parentId)
             .Select(t => t.Order)
             .DefaultIfEmpty(-1)
             .Max() + 1;

    private void SeedFirstList()
    {
        if (Lists.Count > 0) return;
        Lists.Add(new TaskList { Name = "My Tasks", Colour = TaskList.Palette[0], Order = 0 });
    }

    private static string? Quarantine()
    {
        try
        {
            var kept = Path.Combine(FolderPath, $"tasks.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(FilePath, kept, overwrite: false);
            return kept;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
