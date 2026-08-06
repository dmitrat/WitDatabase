using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace OutWit.Database.Studio.Controls;

/// <summary>
/// Draws the wavy line under the mistake, where the mistake is (3.6).
///
/// The position is not computed here and never was: stage 3 built <c>SqlScript.ErrorFor</c> and
/// <c>ToScriptPosition</c> to move an engine's coordinates back into the tab's, and the ViewModel
/// carries the answer. This only draws it - which is the whole reason the underlining could be built
/// in an afternoon rather than a week.
/// </summary>
public sealed class SqlErrorUnderline : IBackgroundRenderer
{
    #region Fields

    private readonly Pen m_pen = new(new SolidColorBrush(Color.FromRgb(0xE5, 0x3E, 0x3E)), 1.4);

    #endregion

    #region Properties

    /// <summary>1-based line, as everything that reports an SQL error counts them. Zero means none.</summary>
    public int Line { get; set; }

    /// <summary>0-based column.</summary>
    public int Column { get; set; }

    /// <summary>How many characters to underline; at least one.</summary>
    public int Length { get; set; }

    public KnownLayer Layer => KnownLayer.Selection;

    #endregion

    #region IBackgroundRenderer

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Line <= 0 || textView.Document == null || Line > textView.Document.LineCount)
            return;

        var documentLine = textView.Document.GetLineByNumber(Line);
        var start = Math.Clamp(documentLine.Offset + Column, documentLine.Offset, documentLine.EndOffset);
        var end = Math.Clamp(start + Math.Max(1, Length), start + 1, documentLine.EndOffset + 1);

        // A mistake at the very end of a line has nothing to underline, so the line itself is marked -
        // silence would read as "there is no error here", which is the opposite of the truth.
        if (start >= documentLine.EndOffset)
        {
            start = Math.Max(documentLine.Offset, documentLine.EndOffset - 1);
            end = documentLine.EndOffset;
        }

        var segment = new AvaloniaEdit.Document.TextSegment { StartOffset = start, EndOffset = end };

        foreach (var rectangle in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            DrawWave(drawingContext, rectangle);
    }

    private void DrawWave(DrawingContext context, Rect rectangle)
    {
        const double STEP = 3.0;

        var y = rectangle.Bottom - 1.5;
        var up = true;
        var previous = new Point(rectangle.Left, y);

        for (var x = rectangle.Left + STEP; x < rectangle.Right; x += STEP)
        {
            var next = new Point(x, up ? y - 2 : y);

            context.DrawLine(m_pen, previous, next);

            previous = next;
            up = !up;
        }
    }

    #endregion
}
