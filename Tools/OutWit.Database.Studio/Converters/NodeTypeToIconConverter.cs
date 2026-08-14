using Avalonia.Data.Converters;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Ui.Icons;
using System.Globalization;

namespace OutWit.Database.Studio.Converters;

/// <summary>
/// Converts DatabaseNodeType to SVG path data string from StudioIcons.
/// Returns string that PathIcon can parse via its built-in converter.
/// </summary>
/// <remarks>
/// <b>The default is a plausible answer, which is what hid the defect.</b> Routines were added to the
/// tree without being added here, so «Процедуры и функции» and every routine under it fell through
/// to the database glyph - and a database glyph beside a folder of functions looks like a decision
/// rather than a fall-through. <c>PATH_DB_ROUTINE</c> had been drawn all along and nothing referenced
/// it.
///
/// <c>SmallSignsTests</c> asserts the whole enum rather than the values that were missing: a rule
/// that names only what was wrong is satisfied the moment it is fixed and says nothing about the
/// next node type somebody adds. It earned that immediately - the record named the routines folder,
/// and the rule found <c>Column</c> as well.
///
/// <b>Not done here, and worth knowing:</b> <c>PATH_DB_KEY</c> and <c>PATH_DB_FOREIGN_KEY</c> are
/// drawn and referenced nowhere. A column node carries <c>IsPrimaryKey</c> and <c>IsForeignKey</c>
/// and the tree draws neither, but this converter is handed a node TYPE and cannot see them - so
/// that is a change to the template rather than a case in this switch.
/// </remarks>
public class NodeTypeToIconConverter : IValueConverter
{
    #region IValueConverter

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DatabaseNodeType nodeType)
            return StudioIcons.PATH_DB_DATABASE;

        return nodeType switch
        {
            DatabaseNodeType.Database => StudioIcons.PATH_DB_DATABASE,
            DatabaseNodeType.TablesFolder => StudioIcons.PATH_COMMON_FOLDER,
            DatabaseNodeType.Table => StudioIcons.PATH_DB_TABLE,
            DatabaseNodeType.ViewsFolder => StudioIcons.PATH_COMMON_FOLDER,
            DatabaseNodeType.View => StudioIcons.PATH_DB_VIEW,
            DatabaseNodeType.IndexesFolder => StudioIcons.PATH_COMMON_FOLDER,
            DatabaseNodeType.Index => StudioIcons.PATH_DB_INDEX,
            DatabaseNodeType.TriggersFolder => StudioIcons.PATH_COMMON_FOLDER,
            DatabaseNodeType.Trigger => StudioIcons.PATH_DB_TRIGGER,
            DatabaseNodeType.SequencesFolder => StudioIcons.PATH_COMMON_FOLDER,
            DatabaseNodeType.Sequence => StudioIcons.PATH_DB_SEQUENCE,
            DatabaseNodeType.RoutinesFolder => StudioIcons.PATH_COMMON_FOLDER,
            DatabaseNodeType.Routine => StudioIcons.PATH_DB_ROUTINE,

            // Columns went into the tree with WS-15 and never got here either. The record named the
            // routines folder; the rule that asserts the whole enum found this one, which is the
            // difference between a rule and a repair.
            DatabaseNodeType.Column => StudioIcons.PATH_COMMON_COLUMNS,
            _ => StudioIcons.PATH_DB_DATABASE
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    #endregion
}
