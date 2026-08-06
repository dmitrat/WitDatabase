using Avalonia.Data.Converters;
using Avalonia.Media;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.Converters;

/// <summary>
/// How a plan operator is drawn (WS-27).
///
/// Three states and no more: access through an index is the good case, a full scan under a filter and
/// a sort under a limit are the two the engine was measured to be slow at, and everything else is
/// neutral. A viewer that colours every node tells nobody anything.
/// </summary>
public static class PlanConverters
{
    public static readonly IValueConverter KindBrush =
        new FuncValueConverter<PlanOperatorKind, IBrush>(kind => kind switch
        {
            PlanOperatorKind.IndexAccess => new SolidColorBrush(Color.FromArgb(0x40, 0x4C, 0xAF, 0x50)),
            PlanOperatorKind.TableScan => new SolidColorBrush(Color.FromArgb(0x40, 0xD9, 0xA4, 0x41)),
            PlanOperatorKind.Sort => new SolidColorBrush(Color.FromArgb(0x30, 0xD9, 0xA4, 0x41)),
            PlanOperatorKind.Join => new SolidColorBrush(Color.FromArgb(0x30, 0x64, 0x95, 0xED)),
            _ => new SolidColorBrush(Color.FromArgb(0x20, 0x80, 0x80, 0x80))
        });
}
