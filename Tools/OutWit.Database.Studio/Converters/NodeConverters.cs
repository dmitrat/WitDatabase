using Avalonia.Data.Converters;
using Avalonia.Media;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Converters;

/// <summary>
/// The two small things a tree row needs to say about a node without a whole template per type.
/// </summary>
public static class NodeConverters
{
    /// <summary>
    /// A NOT NULL column is named in bold (2.1). The type is on the right; the weight carries the
    /// one bit of information a person scanning a column list is actually looking for.
    /// </summary>
    public static readonly IValueConverter RequiredToWeight =
        new FuncValueConverter<bool, FontWeight>(required => required ? FontWeight.SemiBold : FontWeight.Normal);

    /// <summary>
    /// Whether the row is still waiting for its count (WS-16). "counting..." rather than a spinner:
    /// the tree is usable while it waits, and a spinner would suggest it is not.
    /// </summary>
    public static readonly IValueConverter IsCounting =
        new FuncValueConverter<RowCountState, bool>(state => state == RowCountState.Counting);
}
