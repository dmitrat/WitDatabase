namespace OutWit.Database.Samples.BlazorWasm.Models;

/// <summary>
/// Note entity stored in WitDatabase.
/// </summary>
public class Note
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public NoteColor Color { get; set; } = NoteColor.Default;
    public bool IsPinned { get; set; }
    public List<string> Tags { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public string Preview => Content.Length > 100 ? Content[..100] + "..." : Content;
}

/// <summary>
/// Note color enumeration with MudBlazor color mapping.
/// </summary>
public enum NoteColor
{
    Default,
    Red,
    Orange,
    Yellow,
    Green,
    Teal,
    Blue,
    Purple,
    Pink
}

/// <summary>
/// Extension methods for NoteColor.
/// </summary>
public static class NoteColorExtensions
{
    public static string ToMudColor(this NoteColor color) => color switch
    {
        NoteColor.Red => "red lighten-4",
        NoteColor.Orange => "orange lighten-4",
        NoteColor.Yellow => "yellow lighten-4",
        NoteColor.Green => "green lighten-4",
        NoteColor.Teal => "teal lighten-4",
        NoteColor.Blue => "blue lighten-4",
        NoteColor.Purple => "purple lighten-4",
        NoteColor.Pink => "pink lighten-4",
        _ => "grey lighten-4"
    };

    public static string ToCssColor(this NoteColor color) => color switch
    {
        NoteColor.Red => "#ffcdd2",
        NoteColor.Orange => "#ffe0b2",
        NoteColor.Yellow => "#fff9c4",
        NoteColor.Green => "#c8e6c9",
        NoteColor.Teal => "#b2dfdb",
        NoteColor.Blue => "#bbdefb",
        NoteColor.Purple => "#e1bee7",
        NoteColor.Pink => "#f8bbd9",
        _ => "#f5f5f5"
    };
}
