using System;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace CaffeineWin.Todo;

/// <summary>One task list in the sidebar. Its colour tints its tasks' tick circles.</summary>
public sealed class TaskList : INotifyPropertyChanged
{
    /// <summary>The palette a list's colour is chosen from, in the order the picker shows them.</summary>
    public static readonly string[] Palette =
    {
        "#5E9E5E", "#4E8B8B", "#4A82C4", "#6B6396", "#B4708E", "#C4694A", "#C1963C", "#7A7A80"
    };

    private string _name = "";
    private string _colour = Palette[0];

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Order { get; set; }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value ?? "";
            Raise(nameof(Name));
            Raise(nameof(DisplayName));
        }
    }

    public string Colour
    {
        get => _colour;
        set
        {
            if (_colour == value) return;
            _colour = string.IsNullOrWhiteSpace(value) ? Palette[0] : value;
            Raise(nameof(Colour));
        }
    }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Untitled list" : Name.Trim();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
