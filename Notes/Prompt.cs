using System;
using System.Text.Json.Serialization;

namespace CaffeineWin.Notes;

/// <summary>
/// One prompt inside a prompt note. The list these live in carries its own order — position in the
/// JSON array is position on screen — so sending a prompt is a move to the end of the list and
/// there is no order field to keep in step with anything.
/// </summary>
public sealed class Prompt
{
    public string Text { get; set; } = "";

    /// <summary>Moved into the Sent section. Sent prompts always follow the unsent ones.</summary>
    public bool Sent { get; set; }

    /// <summary>When it was marked sent, or null while it is still waiting to go.</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>A card opened and never typed into is a draft, not content.</summary>
    [JsonIgnore] public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    /// <summary>
    /// Stamp for the line under a sent prompt. Shorter than the editor's date but it keeps the
    /// time, because knowing when you sent something is the whole point of the Sent section.
    /// </summary>
    public static string FormatSentAt(DateTime sent, DateTime now)
    {
        var days = now.Date.Subtract(sent.Date).Days;

        return days switch
        {
            0 => $"Sent today {sent:HH:mm}",
            1 => $"Sent yesterday {sent:HH:mm}",
            > 1 and < 7 => $"Sent {sent:ddd} {sent:HH:mm}",
            _ => $"Sent {sent:d MMM} {sent:HH:mm}"
        };
    }
}
