using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace CaffeineWin.Notes;

/// <summary>
/// Two-part storage under %AppData%\Caffeine. <c>notes.json</c> is a small index — titles, plain-text
/// mirrors, timestamps — and each note's formatted body is its own file in <c>bodies\</c>. Keeping
/// bodies out of the index matters once notes contain images: the index stays readable and cheap to
/// rewrite on every keystroke's debounce, and a 2 MB screenshot doesn't get re-serialised with it.
/// </summary>
public sealed class NotesStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Caffeine");

    public static string FilePath => Path.Combine(FolderPath, "notes.json");
    public static string BodyFolder => Path.Combine(FolderPath, "bodies");

    /// <summary>A XAML package: the formatted body plus any images it contains.</summary>
    public static string BodyPath(string id) => Path.Combine(BodyFolder, $"{id}.xamlpkg");

    public ObservableCollection<Note> Notes { get; } = new();

    /// <summary>Set when the last load or save failed. The window surfaces this — saves must never fail silently.</summary>
    public string? LastError { get; private set; }

    public void Load()
    {
        LastError = null;
        Notes.Clear();

        if (!File.Exists(FilePath)) return;

        string json;
        try
        {
            json = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"Couldn't open notes.json — {ex.Message}";
            return;
        }

        List<Note>? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<List<Note>>(json);
        }
        catch (JsonException ex)
        {
            // Never let a save overwrite a file we failed to parse — set it aside first.
            var kept = Quarantine();
            LastError = kept == null
                ? $"notes.json is unreadable — {ex.Message}"
                : $"notes.json was unreadable and has been kept as {Path.GetFileName(kept)}";
            return;
        }

        if (loaded == null) return;

        var rewrite = false;

        foreach (var note in loaded)
        {
            // Notes in Recently Deleted are purged once their retention runs out. Doing it on load
            // keeps the rule in one place, and rewriting the file means they are really gone.
            if (note.IsDeleted && note.DaysLeft <= 0)
            {
                DeleteBody(note.Id);
                rewrite = true;
                continue;
            }

            // Written by versions before rich text: fold the single body string into a title and a
            // plain-text mirror. The body file gets written the first time the note is opened.
            if (!string.IsNullOrEmpty(note.Body))
            {
                var (title, remainder) = Note.SplitLegacyBody(note.Body);
                if (string.IsNullOrWhiteSpace(note.Title)) note.Title = title;
                if (string.IsNullOrWhiteSpace(note.PlainText)) note.PlainText = remainder;
                note.Body = null;
                rewrite = true;
            }

            Notes.Add(note);
        }

        if (rewrite) Save();
    }

    /// <summary>Writes the index via a temporary file so a crash mid-write cannot tear notes.json.</summary>
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);

            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new List<Note>(Notes), SerializerOptions));
            File.Move(temp, FilePath, overwrite: true);

            LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"Couldn't save notes — {ex.Message}";
            return false;
        }
    }

    // ===== Bodies =====

    /// <summary>
    /// Hands a stream to the caller to write the formatted body into, then moves it into place. The
    /// document format is the view's business; the store only owns the file.
    /// </summary>
    public bool SaveBody(string id, Action<Stream> write)
    {
        try
        {
            Directory.CreateDirectory(BodyFolder);

            var temp = BodyPath(id) + ".tmp";
            using (var stream = File.Create(temp))
                write(stream);

            File.Move(temp, BodyPath(id), overwrite: true);
            LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"Couldn't save this note's content — {ex.Message}";
            return false;
        }
    }

    /// <summary>Reads a body if one exists. False means there is no file — the caller falls back to plain text.</summary>
    public bool LoadBody(string id, Action<Stream> read)
    {
        var path = BodyPath(id);
        if (!File.Exists(path)) return false;

        try
        {
            using var stream = File.OpenRead(path);
            read(stream);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"Couldn't open this note's content — {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            // A body that will not deserialise shouldn't take the app down; fall back to plain text.
            LastError = $"This note's formatting couldn't be read — {ex.Message}";
            return false;
        }
    }

    public void DeleteBody(string id)
    {
        try
        {
            var path = BodyPath(id);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"Couldn't remove this note's content — {ex.Message}";
        }
    }

    private static string? Quarantine()
    {
        try
        {
            var kept = Path.Combine(FolderPath, $"notes.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(FilePath, kept, overwrite: false);
            return kept;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
