using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.Controls;

/// <summary>
/// What the editor needs to know about one suggestion, wrapped around what completion produces.
///
/// <see cref="SqlCompletionItem"/> deliberately knows nothing about AvaloniaEdit - it is a service
/// answer, and it is tested without a window anywhere near it. This is the adapter, and it is the only
/// part of completion that cannot be tested without a UI.
/// </summary>
public sealed class SqlCompletionData(SqlCompletionItem item, int replaceFrom) : ICompletionData
{
    #region ICompletionData

    public IImage? Image => null;

    public string Text => item.Text;

    /// <summary>
    /// The two lines the design asks for: the name with its kind on the right, and what it belongs to
    /// underneath.
    /// </summary>
    public object Content => item.Detail == null ? item.Text : $"{item.Text}    {item.Detail}";

    public object? Description => item.Description ?? item.Detail;

    /// <summary>
    /// AvaloniaEdit sorts by this descending. The order was already decided by
    /// <see cref="SqlCompletion"/>, which knows what the caret is in front of, so the list keeps the
    /// order it was given rather than being re-sorted by a window that knows less.
    /// </summary>
    public double Priority { get; set; }

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        // The word being typed is what gets replaced, not the segment AvaloniaEdit guesses at: the
        // guess starts at the last non-identifier character, which is the dot in "o.Tot".
        var start = Math.Clamp(replaceFrom, 0, textArea.Document.TextLength);
        var end = Math.Max(start, completionSegment.EndOffset);

        textArea.Document.Replace(start, end - start, item.Text);
    }

    #endregion

    #region Properties

    public SqlCompletionItem Item => item;

    #endregion
}
