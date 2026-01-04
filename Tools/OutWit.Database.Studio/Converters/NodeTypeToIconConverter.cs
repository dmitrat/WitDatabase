using Avalonia.Data.Converters;
using OutWit.Database.Studio.Models;
using System;
using System.Globalization;

namespace OutWit.Database.Studio.Converters;

/// <summary>
/// Converts DatabaseNodeType to Material Design icon path data.
/// Returns SVG path data string that can be used with PathIcon.
/// </summary>
public class NodeTypeToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DatabaseNodeType nodeType)
            return GetDefaultIconPath();

        return nodeType switch
        {
            // Database - Storage icon
            DatabaseNodeType.Database => "M2 6V8H14V6H2M2 10V12H14V10H2M20 10.1C19.9 10.1 19.7 10.2 19.6 10.3L18.6 11.3L20.7 13.4L21.7 12.4C21.9 12.2 21.9 11.8 21.7 11.6L20.4 10.3C20.3 10.2 20.2 10.1 20 10.1M18.1 11.9L12 17.9V20H14.1L20.2 13.9L18.1 11.9Z",
            
            // Tables Folder - Folder icon
            DatabaseNodeType.TablesFolder => "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z",
            
            // Table - Table icon  
            DatabaseNodeType.Table => "M5,4H19A2,2 0 0,1 21,6V18A2,2 0 0,1 19,20H5A2,2 0 0,1 3,18V6A2,2 0 0,1 5,4M5,8V12H11V8H5M13,8V12H19V8H13M5,14V18H11V14H5M13,14V18H19V14H13Z",
            
            // Views Folder - Folder with eye
            DatabaseNodeType.ViewsFolder => "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z",
            
            // View - Eye icon
            DatabaseNodeType.View => "M12,9A3,3 0 0,0 9,12A3,3 0 0,0 12,15A3,3 0 0,0 15,12A3,3 0 0,0 12,9M12,17A5,5 0 0,1 7,12A5,5 0 0,1 12,7A5,5 0 0,1 17,12A5,5 0 0,1 12,17M12,4.5C7,4.5 2.73,7.61 1,12C2.73,16.39 7,19.5 12,19.5C17,19.5 21.27,16.39 23,12C21.27,7.61 17,4.5 12,4.5Z",
            
            // Indexes Folder - Folder with magnifying glass
            DatabaseNodeType.IndexesFolder => "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z",
            
            // Index - Flash/Lightning icon
            DatabaseNodeType.Index => "M7,2V13H10V22L17,10H13L17,2H7Z",
            
            // Triggers Folder - Folder with flash
            DatabaseNodeType.TriggersFolder => "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z",
            
            // Trigger - Flash outline icon
            DatabaseNodeType.Trigger => "M11,15H13V17H11V15M11,7H13V13H11V7M12,2C6.47,2 2,6.5 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20Z",
            
            // Sequences Folder - Folder with counter
            DatabaseNodeType.SequencesFolder => "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z",
            
            // Sequence - Counter icon
            DatabaseNodeType.Sequence => "M4,4H7V14H9V16H4V14H6V6H4V4M13,4H16V6H11V8H14A2,2 0 0,1 16,10V11A2,2 0 0,1 14,13H10V11H14V10H11A2,2 0 0,1 9,8V7A2,2 0 0,1 11,5H13V4M4,18H7V20H4V18M13,18H16V20H13V18M8.5,18H11.5V20H8.5V18Z",
            
            _ => GetDefaultIconPath()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static string GetDefaultIconPath()
    {
        // Default - File document icon
        return "M13,9H18.5L13,3.5V9M6,2H14L20,8V20A2,2 0 0,1 18,22H6C4.89,22 4,21.1 4,20V4C4,2.89 4.89,2 6,2M15,18V16H6V18H15M18,14V12H6V14H18Z";
    }
}
