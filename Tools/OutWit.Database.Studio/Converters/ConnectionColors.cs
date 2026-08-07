using Avalonia.Data.Converters;
using Avalonia.Media;

namespace OutWit.Database.Studio.Converters;

/// <summary>
/// The colours that mark a connection (WS-3, WS-46).
///
/// <para>
/// One palette, in one place, used by three things that must agree: the swatch row in the Open dialog
/// where the colour is chosen, the stripe down the side of a tab where it is read, and the connection
/// chip in the toolbar. Three separate lists of colours would drift, and a stripe that is not the
/// colour the user picked is worse than no stripe.
/// </para>
/// <para>
/// The colour is not decoration. With several databases open, it is what answers "where is this query
/// about to go" before the query runs rather than after.
/// </para>
/// </summary>
public static class ConnectionColors
{
    #region Constants

    /// <summary>
    /// Six, matching <c>ConnectionManager.COLOR_COUNT</c>. Chosen to stay apart from each other in
    /// both themes and to avoid the red - which is reserved for a connection that has a problem.
    /// </summary>
    public static readonly IReadOnlyList<Color> Palette =
    [
        Color.FromRgb(0x4C, 0xC1, 0x3C), // lime
        Color.FromRgb(0xD9, 0xA2, 0x27), // amber
        Color.FromRgb(0x8A, 0x7B, 0xE8), // violet
        Color.FromRgb(0x35, 0xA7, 0xC4), // cyan
        Color.FromRgb(0xD9, 0x53, 0x4F), // red
        Color.FromRgb(0x5D, 0x68, 0x79)  // grey
    ];

    #endregion

    #region Converters

    /// <summary>
    /// A colour index to the brush that draws it. An index outside the palette answers with the last
    /// colour rather than throwing: the index comes out of a saved list a person can edit.
    /// </summary>
    public static readonly IValueConverter Brush =
        new FuncValueConverter<int, IBrush>(index => new SolidColorBrush(At(index)));

    /// <summary>The colour at an index, wrapped into the palette.</summary>
    public static Color At(int index)
    {
        if (Palette.Count == 0)
            return Colors.Transparent;

        var wrapped = index < 0 ? Palette.Count - 1 : index % Palette.Count;

        return Palette[wrapped];
    }

    #endregion
}
