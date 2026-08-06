using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OutWit.Database.Studio.Models;

/// <summary>
/// One box of the filter row (4.3): which column, and what was typed into it.
///
/// A ViewModel rather than a record because the box is bound two ways and has to notice being typed
/// into; an active one is coloured, an empty one is transparent.
/// </summary>
public sealed class ColumnFilter : INotifyPropertyChanged
{
    private string? m_text;

    public ColumnFilter(string column, string type)
    {
        Column = column;
        Type = type;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Column { get; }

    public string Type { get; }

    public string? Text
    {
        get => m_text;
        set
        {
            if (m_text == value)
                return;

            m_text = value;

            Raise();
            Raise(nameof(IsActive));
        }
    }

    /// <summary>
    /// A box with something in it is coloured; an empty one is transparent (4.1).
    /// </summary>
    public bool IsActive => !string.IsNullOrWhiteSpace(Text);

    private void Raise([CallerMemberName] string? property = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

/// <summary>
/// One column where the user's value and the database's have parted company (WS-37).
///
/// Both sides, always: "the transaction was rejected" is what this replaces, and it leaves somebody
/// with a decision to make and nothing to make it from.
/// </summary>
/// <param name="Column">Which column.</param>
/// <param name="Mine">The value in the edit buffer, as SQL would write it.</param>
/// <param name="Theirs">The value in the database now.</param>
public sealed record ConflictedValue(string Column, string Mine, string Theirs);
