using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace OutWit.Database.Studio.Converters;

/// <summary>
/// Widths for the panels of the frame.
///
/// A hidden control does NOT collapse the grid column it stands in: a column declared as 320 keeps
/// its 320 pixels whether anything is drawn there or not. The inspector is hidden until something is
/// open, and without this the welcome screen was centred in a window 324 pixels narrower than itself
/// with a dead band down the right - which reads as the whole application being shifted sideways.
/// </summary>
public static class PanelConverters
{
    /// <summary>
    /// The inspector's column: its width when there is something to inspect, nothing when there is
    /// not. A GridSplitter still sets a width of its own on top of this while the panel is open.
    /// </summary>
    public static readonly IValueConverter InspectorWidth =
        new FuncValueConverter<bool, GridLength>(connected => new GridLength(connected ? 320 : 0));

    /// <summary>
    /// The splitter beside it, which has the same problem four pixels wide.
    /// </summary>
    public static readonly IValueConverter SplitterWidth =
        new FuncValueConverter<bool, GridLength>(connected => new GridLength(connected ? 4 : 0));

    /// <summary>
    /// The transaction indicator in the status bar (WS-26). Autocommit is the quiet state and gets no
    /// colour at all; an open transaction is the one that is expensive to forget about, so it is the
    /// only thing in the status bar that is ever highlighted.
    /// </summary>
    public static readonly IValueConverter TransactionBrush =
        new FuncValueConverter<bool, Avalonia.Media.IBrush>(open => open
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0x60, 0xD9, 0xA4, 0x41))
            : Avalonia.Media.Brushes.Transparent);
}
