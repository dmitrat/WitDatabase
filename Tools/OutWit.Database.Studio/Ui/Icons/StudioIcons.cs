using Avalonia.Media;

namespace OutWit.Database.Studio.Ui.Icons;

/// <summary>
/// Central storage for SVG path icons used in XAML.
/// Uses lazy initialization to avoid Avalonia runtime dependency issues in tests.
/// </summary>
public static class StudioIcons
{
    #region Path Data Constants

    // Store path data as strings for use in converters and tests
    public const string PATH_MENU_NEW_DATABASE = "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z";
    public const string PATH_MENU_OPEN_DATABASE = "M19,20H4C2.89,20 2,19.1 2,18V6C2,4.89 2.89,4 4,4H10L12,6H19A2,2 0 0,1 21,8H21L4,8V18L6.14,10H23.21L20.93,18.5C20.7,19.37 19.92,20 19,20Z";
    public const string PATH_MENU_CLOSE_DATABASE = "M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z";
    public const string PATH_MENU_RECENT_FILES = "M13.5,8H12V13L16.28,15.54L17,14.33L13.5,12.25V8M13,3A9,9 0 0,0 4,12H1L4.96,16.03L9,12H6A7,7 0 0,1 13,5A7,7 0 0,1 20,12A7,7 0 0,1 13,19C11.07,19 9.32,18.21 8.06,16.94L6.64,18.36C8.27,20 10.5,21 13,21A9,9 0 0,0 22,12A9,9 0 0,0 13,3";
    public const string PATH_MENU_EXIT = "M16,17V14H9V10H16V7L21,12L16,17M14,2A2,2 0 0,1 16,4V6H14V4H5V20H14V18H16V20A2,2 0 0,1 14,22H5A2,2 0 0,1 3,20V4A2,2 0 0,1 5,2H14Z";
    public const string PATH_MENU_COPY = "M19,21H8V7H19M19,5H8A2,2 0 0,0 6,7V21A2,2 0 0,0 8,23H19A2,2 0 0,0 21,21V7A2,2 0 0,0 19,5M16,1H4A2,2 0 0,0 2,3V17H4V3H16V1Z";
    public const string PATH_MENU_PASTE = "M19,20H5V4H7V7H17V4H19M12,2A1,1 0 0,1 13,3A1,1 0 0,1 12,4A1,1 0 0,1 11,3A1,1 0 0,1 12,2M19,2H14.82C14.4,0.84 13.3,0 12,0C10.7,0 9.6,0.84 9.18,2H5A2,2 0 0,0 3,4V20A2,2 0 0,0 5,22H19A2,2 0 0,0 21,20V4A2,2 0 0,0 19,2Z";
    public const string PATH_MENU_REFRESH = "M17.65,6.35C16.2,4.9 14.21,4 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20C15.73,20 18.84,17.45 19.73,14H17.65C16.83,16.33 14.61,18 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z";
    public const string PATH_MENU_EXPORT = "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M13.5,16V19L10.5,16H13.5M6,20V4H13V9H18V20H6Z";
    public const string PATH_MENU_IMPORT = "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M13.5,16L10.5,13H13.5V10H13.5V13L16.5,16H13.5M6,20V4H13V9H18V20H6Z";
    public const string PATH_MENU_ABOUT = "M11,9H13V7H11M12,20C7.59,20 4,16.41 4,12C4,7.59 7.59,4 12,4C16.41,4 20,7.59 20,12C20,16.41 16.41,20 12,20M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M11,17H13V11H11V17Z";
    public const string PATH_COMMON_BULLET = "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2Z";
    public const string PATH_COMMON_WARNING = "M1,21H23L12,2L1,21M12,16A1,1 0 0,0 13,15A1,1 0 0,0 12,14A1,1 0 0,0 11,15A1,1 0 0,0 12,16M11,10V13H13V10H11Z";
    public const string PATH_COMMON_FOLDER = "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z";
    public const string PATH_COMMON_DELETE = "M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z";
    public const string PATH_QUERY_EXECUTE = "M8,5.14V19.14L19,12.14L8,5.14Z";
    public const string PATH_QUERY_STOP = "M6,6H18V18H6V6Z";
    public const string PATH_QUERY_SAVE = "M15,9H5V5H15M12,19A3,3 0 0,1 9,16A3,3 0 0,1 12,13A3,3 0 0,1 15,16A3,3 0 0,1 12,19M17,3H5C3.89,3 3,3.9 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V7L17,3Z";
    public const string PATH_QUERY_CLEAR = "M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z";
    public const string PATH_TAB_CLOSE = "M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z";
    public const string PATH_TAB_NEW = "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z";
    public const string PATH_TAB_MODIFIED = "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z";
    public const string PATH_PAGE_FIRST = "M18.41,16.59L13.82,12L18.41,7.41L17,6L11,12L17,18L18.41,16.59M6,6H8V18H6V6Z";
    public const string PATH_PAGE_PREVIOUS = "M15.41,16.58L10.83,12L15.41,7.41L14,6L8,12L14,18L15.41,16.58Z";
    public const string PATH_PAGE_NEXT = "M8.59,16.58L13.17,12L8.59,7.41L10,6L16,12L10,18L8.59,16.58Z";
    public const string PATH_PAGE_LAST = "M5.59,7.41L10.18,12L5.59,16.59L7,18L13,12L7,6L5.59,7.41M16,6H18V18H16V6Z";
    public const string PATH_ARROW_UP = "M7.41,15.41L12,10.83L16.59,15.41L18,14L12,8L6,14L7.41,15.41Z";
    public const string PATH_ARROW_DOWN = "M7.41,8.58L12,13.17L16.59,8.58L18,10L12,16L6,10L7.41,8.58Z";
    public const string PATH_ARROW_LEFT = "M15.41,16.58L10.83,12L15.41,7.41L14,6L8,12L14,18L15.41,16.58Z";
    public const string PATH_ARROW_RIGHT = "M8.59,16.58L13.17,12L8.59,7.41L10,6L16,12L10,18L8.59,16.58Z";
    public const string PATH_DB_DATABASE = "M12,3C7.58,3 4,4.79 4,7C4,9.21 7.58,11 12,11C16.42,11 20,9.21 20,7C20,4.79 16.42,3 12,3M4,9V12C4,14.21 7.58,16 12,16C16.42,16 20,14.21 20,12V9C20,11.21 16.42,13 12,13C7.58,13 4,11.21 4,9M4,14V17C4,19.21 7.58,21 12,21C16.42,21 20,19.21 20,17V14C20,16.21 16.42,18 12,18C7.58,18 4,16.21 4,14Z";
    public const string PATH_DB_TABLE = "M5,4H19A2,2 0 0,1 21,6V18A2,2 0 0,1 19,20H5A2,2 0 0,1 3,18V6A2,2 0 0,1 5,4M5,8V12H11V8H5M13,8V12H19V8H13M5,14V18H11V14H5M13,14V18H19V14H13Z";
    public const string PATH_DB_VIEW = "M12,9A3,3 0 0,0 9,12A3,3 0 0,0 12,15A3,3 0 0,0 15,12A3,3 0 0,0 12,9M12,17A5,5 0 0,1 7,12A5,5 0 0,1 12,7A5,5 0 0,1 17,12A5,5 0 0,1 12,17M12,4.5C7,4.5 2.73,7.61 1,12C2.73,16.39 7,19.5 12,19.5C17,19.5 21.27,16.39 23,12C21.27,7.61 17,4.5 12,4.5Z";
    public const string PATH_DB_INDEX = "M7,2V13H10V22L17,10H13L17,2H7Z";
    public const string PATH_DB_TRIGGER = "M11,15H13V17H11V15M11,7H13V13H11V7M12,2C6.47,2 2,6.5 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20Z";
    public const string PATH_DB_SEQUENCE = "M4,4H7V14H9V16H4V14H6V6H4V4M13,4H16V6H11V8H14A2,2 0 0,1 16,10V11A2,2 0 0,1 14,13H10V11H14V10H11A2,2 0 0,1 9,8V7A2,2 0 0,1 11,5H13V4M4,18H7V20H4V18M13,18H16V20H13V18M8.5,18H11.5V20H8.5V18Z";
    public const string PATH_COPY = "M19,21H8V7H19M19,5H8A2,2 0 0,0 6,7V21A2,2 0 0,0 8,23H19A2,2 0 0,0 21,21V7A2,2 0 0,0 19,5M16,1H4A2,2 0 0,0 2,3V17H4V3H16V1Z";
    public const string PATH_EXPORT = "M5,20H19V18H5M19,9H15V3H9V9H5L12,16L19,9Z";
    public const string PATH_COPY_AS_INSERT = "M17,9H7V7H17M17,13H7V11H17M14,17H7V15H14M12,3A9,9 0 0,1 21,12A9,9 0 0,1 12,21A9,9 0 0,1 3,12A9,9 0 0,1 12,3Z";

    // Table Editor icons
    public const string PATH_TABLE_EDITOR_ADD_ROW = "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z";
    public const string PATH_TABLE_EDITOR_DELETE_ROW = "M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z";
    public const string PATH_TABLE_EDITOR_COMMIT = "M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z";
    public const string PATH_TABLE_EDITOR_ROLLBACK = "M12.5,8C9.85,8 7.45,9 5.6,10.6L2,7V16H11L7.38,12.38C8.77,11.22 10.54,10.5 12.5,10.5C16.04,10.5 19.05,12.81 20.1,16L22.47,15.22C21.08,11.03 17.15,8 12.5,8Z";

    // Workspace Tab icons
    public const string PATH_TAB_QUERY = "M8,5.14V19.14L19,12.14L8,5.14Z";  // Same as execute
    public const string PATH_TAB_TABLE_EDIT = "M21.7,13.35L20.7,14.35L18.65,12.3L19.65,11.3C19.86,11.08 20.21,11.08 20.42,11.3L21.7,12.58C21.92,12.79 21.92,13.14 21.7,13.35M12,18.94L18.07,12.88L20.12,14.93L14.06,21H12V18.94M4,2H18A2,2 0 0,1 20,4V8.17L16.17,12H12V16.17L10.17,18H4A2,2 0 0,1 2,16V4A2,2 0 0,1 4,2M4,6V10H10V6H4M12,6V10H18V6H12M4,12V16H10V12H4Z";
    public const string PATH_TAB_STRUCTURE = "M12,3C7.58,3 4,4.79 4,7V17C4,19.21 7.59,21 12,21C16.42,21 20,19.21 20,17V7C20,4.79 16.42,3 12,3M12,5C15.87,5 18,6.5 18,7C18,7.5 15.87,9 12,9C8.13,9 6,7.5 6,7C6,6.5 8.13,5 12,5M18,17C18,17.5 15.87,19 12,19C8.13,19 6,17.5 6,17V14.77C7.61,15.55 9.72,16 12,16C14.28,16 16.39,15.55 18,14.77V17M18,12.45C16.7,13.4 14.42,14 12,14C9.58,14 7.3,13.4 6,12.45V9.64C7.47,10.47 9.61,11 12,11C14.39,11 16.53,10.47 18,9.64V12.45Z";
    public const string PATH_TAB_PIN = "M16,12V4H17V2H7V4H8V12L6,14V16H11.2V22H12.8V16H18V14L16,12Z";
    public const string PATH_TAB_UNPIN = "M16,12V4H17V2H7V4H8V12L6,14V16H11.2V22H12.8V16H18V14L16,12M8.8,14L10,12.8V4H14V12.8L15.2,14H8.8Z";

    #endregion

    #region Menu

    public static StreamGeometry MENU_NEW_DATABASE => StreamGeometry.Parse(PATH_MENU_NEW_DATABASE);
    public static StreamGeometry MENU_OPEN_DATABASE => StreamGeometry.Parse(PATH_MENU_OPEN_DATABASE);
    public static StreamGeometry MENU_CLOSE_DATABASE => StreamGeometry.Parse(PATH_MENU_CLOSE_DATABASE);
    public static StreamGeometry MENU_RECENT_FILES => StreamGeometry.Parse(PATH_MENU_RECENT_FILES);
    public static StreamGeometry MENU_EXIT => StreamGeometry.Parse(PATH_MENU_EXIT);
    public static StreamGeometry MENU_COPY => StreamGeometry.Parse(PATH_MENU_COPY);
    public static StreamGeometry MENU_PASTE => StreamGeometry.Parse(PATH_MENU_PASTE);
    public static StreamGeometry MENU_REFRESH => StreamGeometry.Parse(PATH_MENU_REFRESH);
    public static StreamGeometry MENU_EXPORT => StreamGeometry.Parse(PATH_MENU_EXPORT);
    public static StreamGeometry MENU_IMPORT => StreamGeometry.Parse(PATH_MENU_IMPORT);
    public static StreamGeometry MENU_ABOUT => StreamGeometry.Parse(PATH_MENU_ABOUT);

    #endregion

    #region Common

    public static StreamGeometry COMMON_BULLET => StreamGeometry.Parse(PATH_COMMON_BULLET);
    public static StreamGeometry COMMON_WARNING => StreamGeometry.Parse(PATH_COMMON_WARNING);
    public static StreamGeometry COMMON_FOLDER => StreamGeometry.Parse(PATH_COMMON_FOLDER);
    public static StreamGeometry COMMON_DELETE => StreamGeometry.Parse(PATH_COMMON_DELETE);

    #endregion

    #region Query Editor

    public static StreamGeometry QUERY_EXECUTE => StreamGeometry.Parse(PATH_QUERY_EXECUTE);
    public static StreamGeometry QUERY_STOP => StreamGeometry.Parse(PATH_QUERY_STOP);
    public static StreamGeometry QUERY_SAVE => StreamGeometry.Parse(PATH_QUERY_SAVE);
    public static StreamGeometry QUERY_CLEAR => StreamGeometry.Parse(PATH_QUERY_CLEAR);
    public static StreamGeometry TAB_CLOSE => StreamGeometry.Parse(PATH_TAB_CLOSE);
    public static StreamGeometry TAB_NEW => StreamGeometry.Parse(PATH_TAB_NEW);
    public static StreamGeometry TAB_MODIFIED => StreamGeometry.Parse(PATH_TAB_MODIFIED);

    #endregion

    #region Pagination

    public static StreamGeometry PAGE_FIRST => StreamGeometry.Parse(PATH_PAGE_FIRST);
    public static StreamGeometry PAGE_PREVIOUS => StreamGeometry.Parse(PATH_PAGE_PREVIOUS);
    public static StreamGeometry PAGE_NEXT => StreamGeometry.Parse(PATH_PAGE_NEXT);
    public static StreamGeometry PAGE_LAST => StreamGeometry.Parse(PATH_PAGE_LAST);

    #endregion

    #region Arrows

    public static StreamGeometry ARROW_UP => StreamGeometry.Parse(PATH_ARROW_UP);
    public static StreamGeometry ARROW_DOWN => StreamGeometry.Parse(PATH_ARROW_DOWN);
    public static StreamGeometry ARROW_LEFT => StreamGeometry.Parse(PATH_ARROW_LEFT);
    public static StreamGeometry ARROW_RIGHT => StreamGeometry.Parse(PATH_ARROW_RIGHT);
    public static StreamGeometry CHEVRON_RIGHT => ARROW_RIGHT;
    public static StreamGeometry CHEVRON_LEFT => ARROW_LEFT;

    #endregion

    #region Database Objects

    public static StreamGeometry DB_DATABASE => StreamGeometry.Parse(PATH_DB_DATABASE);
    public static StreamGeometry DB_TABLE => StreamGeometry.Parse(PATH_DB_TABLE);
    public static StreamGeometry DB_VIEW => StreamGeometry.Parse(PATH_DB_VIEW);
    public static StreamGeometry DB_INDEX => StreamGeometry.Parse(PATH_DB_INDEX);
    public static StreamGeometry DB_TRIGGER => StreamGeometry.Parse(PATH_DB_TRIGGER);
    public static StreamGeometry DB_SEQUENCE => StreamGeometry.Parse(PATH_DB_SEQUENCE);

    #endregion

    #region Copy/Export

    public static StreamGeometry COPY => StreamGeometry.Parse(PATH_COPY);
    public static StreamGeometry EXPORT => StreamGeometry.Parse(PATH_EXPORT);
    public static StreamGeometry COPY_AS_INSERT => StreamGeometry.Parse(PATH_COPY_AS_INSERT);

    #endregion

    #region Table Editor

    public static StreamGeometry TABLE_EDITOR_ADD_ROW => StreamGeometry.Parse(PATH_TABLE_EDITOR_ADD_ROW);
    public static StreamGeometry TABLE_EDITOR_DELETE_ROW => StreamGeometry.Parse(PATH_TABLE_EDITOR_DELETE_ROW);
    public static StreamGeometry TABLE_EDITOR_COMMIT => StreamGeometry.Parse(PATH_TABLE_EDITOR_COMMIT);
    public static StreamGeometry TABLE_EDITOR_ROLLBACK => StreamGeometry.Parse(PATH_TABLE_EDITOR_ROLLBACK);

    #endregion

    #region Workspace Tabs

    public static StreamGeometry TAB_QUERY => StreamGeometry.Parse(PATH_TAB_QUERY);
    public static StreamGeometry TAB_TABLE_EDIT => StreamGeometry.Parse(PATH_TAB_TABLE_EDIT);
    public static StreamGeometry TAB_STRUCTURE => StreamGeometry.Parse(PATH_TAB_STRUCTURE);
    public static StreamGeometry TAB_PIN => StreamGeometry.Parse(PATH_TAB_PIN);
    public static StreamGeometry TAB_UNPIN => StreamGeometry.Parse(PATH_TAB_UNPIN);

    #endregion

    #region Views

    public static StreamGeometry TABLE_STRUCTURE_REFRESH => MENU_REFRESH;

    #endregion
}
