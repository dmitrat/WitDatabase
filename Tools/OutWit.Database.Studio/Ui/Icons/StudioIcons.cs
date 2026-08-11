using Avalonia.Media;

namespace OutWit.Database.Studio.Ui.Icons;

/// <summary>
/// One icon style for the whole product: OUTLINE, <c>viewBox 24x24</c>, stroke 1.7, round caps, and
/// <b>no fills</b> (section 8.3).
///
/// <para>
/// <b>Why every path in this file was redrawn rather than restyled.</b> The set that shipped was
/// Material's FILLED geometry - a table drawn as a rectangle with rectangular holes punched in it, a
/// database as three stacked solid discs. Stroking the outline of a filled shape does not produce the
/// outline drawing of the same thing; it produces the boundary of the ink, which for a punched
/// rectangle is six loops. So the style could not be changed without changing the geometry, and this
/// file is the geometry.
/// </para>
/// <para>
/// <b>Colour is inherited from the text and that is the point of the style.</b> An outline glyph with
/// no fill takes the row's foreground, so it is automatically right in the dark theme, in the light
/// theme and in the disabled state - which is three problems a filled glyph with an explicit brush
/// has to solve one at a time. Section 8.3 allows exactly three exceptions and they are stated where
/// they are used: a primary key is the accent, a warning is amber, an error is red.
/// </para>
/// <para>
/// <b>The canon names 24 icons and this application uses fifty.</b> Twenty-two of the canon's have a
/// consumer here; the rest of the file is the other twenty-eight, drawn to the same rules, because a
/// half-converted set is worse than a consistent wrong one. Where the canon has a drawing, the
/// canon's drawing is used verbatim.
/// </para>
/// <para>
/// <b>Circles, rectangles and ellipses are path data here.</b> The canon draws them as SVG shapes;
/// Avalonia parses one path string per icon, so each becomes arcs and lines - a circle at
/// <c>(cx,cy,r)</c> is <c>M{cx-r},{cy} A{r},{r} 0 1,0 {cx+r},{cy} A{r},{r} 0 1,0 {cx-r},{cy}</c>. The
/// conversion is mechanical and the shapes are unchanged.
/// </para>
/// </summary>
public static class StudioIcons
{
    #region Path Data Constants

    // ---------------------------------------------------------------- primitives, shared by name

    /// <summary>A plus. Adding a row, a tab and a database are the same gesture.</summary>
    public const string PATH_PLUS = "M12 5v14M5 12h14";

    /// <summary>A cross. Closing a tab, a database and a dialog.</summary>
    public const string PATH_CLOSE = "M6 6l12 12M18 6L6 18";

    /// <summary>The canon's trash: a lid, a handle and a tapering body.</summary>
    public const string PATH_TRASH = "M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13";

    /// <summary>The canon's check.</summary>
    public const string PATH_CHECK = "M4 12l5 5L20 6";

    /// <summary>The canon's undo.</summary>
    public const string PATH_UNDO = "M4 9h11a5 5 0 0 1 0 10h-6M8 5L4 9l4 4";

    /// <summary>The canon's refresh - an open ring, so the gap reads as motion.</summary>
    public const string PATH_REFRESH = "M20 12a8 8 0 1 1-2.4-5.7M20 4v5h-5";

    /// <summary>The canon's folder is not in section 8.3; the tree has six of them.</summary>
    public const string PATH_FOLDER = "M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z";

    // ---------------------------------------------------------------- menu

    public const string PATH_MENU_NEW_DATABASE = PATH_PLUS;
    public const string PATH_MENU_OPEN_DATABASE = PATH_FOLDER;
    public const string PATH_MENU_CLOSE_DATABASE = PATH_CLOSE;
    public const string PATH_MENU_RECENT_FILES = "M3 12a9 9 0 1 0 18 0 9 9 0 1 0-18 0M12 7v5l3.5 2";
    public const string PATH_MENU_EXIT = "M14 4h4a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-4M9 16l-4-4 4-4M5 12h10";
    public const string PATH_MENU_COPY = "M9 9h10a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1V10a1 1 0 0 1 1-1zM5 15H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v1";
    public const string PATH_MENU_PASTE = "M9 4h6v3H9zM8 5H6a1 1 0 0 0-1 1v13a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V6a1 1 0 0 0-1-1h-2";
    public const string PATH_MENU_REFRESH = PATH_REFRESH;
    public const string PATH_MENU_EXPORT = "M12 15V4M8 8l4-4 4 4M5 14v5h14v-5";
    public const string PATH_MENU_IMPORT = "M12 4v11M8 11l4 4 4-4M5 14v5h14v-5";
    public const string PATH_MENU_ABOUT = "M3 12a9 9 0 1 0 18 0 9 9 0 1 0-18 0M12 11v6M12 7.5h.01";

    // ---------------------------------------------------------------- common

    /// <summary>A dot. The one glyph that is a FILL by nature - a marker, not a drawing.</summary>
    public const string PATH_COMMON_BULLET = "M12 9a3 3 0 1 0 0 6 3 3 0 1 0 0-6z";

    public const string PATH_COMMON_SEARCH = "M5 11a6 6 0 1 0 12 0 6 6 0 1 0-12 0M20 20l-4.5-4.5";
    public const string PATH_COMMON_BELL = "M18 16v-5a6 6 0 0 0-12 0v5l-2 2h16zM10 20.5a2 2 0 0 0 4 0";
    public const string PATH_COMMON_WARNING = "M12 4l9 16H3zM12 10v4M12 17h.01";
    public const string PATH_COMMON_ERROR = "M3 12a9 9 0 1 0 18 0 9 9 0 1 0-18 0M15 9l-6 6M9 9l6 6";
    public const string PATH_COMMON_FOLDER = PATH_FOLDER;
    public const string PATH_COMMON_DELETE = PATH_TRASH;
    public const string PATH_COMMON_FILTER = "M3 5h18l-7 8v6l-4-2v-4z";
    public const string PATH_COMMON_COLUMNS = "M3 5h18v14H3zM9 5v14M15 5v14";
    public const string PATH_COMMON_GEAR = "M9 12a3 3 0 1 0 6 0 3 3 0 1 0-6 0M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M18.4 5.6l-2.1 2.1M7.7 16.3l-2.1 2.1";
    public const string PATH_COMMON_LOCK = "M7 10h10a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2v-6a2 2 0 0 1 2-2zM8 10V7a4 4 0 0 1 8 0v3";

    // ---------------------------------------------------------------- query

    public const string PATH_QUERY_EXECUTE = "M7 4l12 8-12 8z";
    public const string PATH_QUERY_STOP = "M8 6h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z";
    public const string PATH_QUERY_SAVE = "M5 4h11l3 3v13a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1zM8 4v5h7V4M8 20v-6h8v6";
    public const string PATH_QUERY_CLEAR = PATH_TRASH;
    public const string PATH_QUERY_FORMAT = "M4 5h16M4 10h10M4 15h16M4 20h8";

    /// <summary>
    /// The whole script: the same triangle, with the lines it is about beside it. Running one
    /// statement and running the file are different actions and the toolbar has both, so they cannot
    /// share a glyph - the icon is the only thing telling them apart at a glance.
    /// </summary>
    public const string PATH_QUERY_EXECUTE_SCRIPT = "M4 6h7M4 11h5M4 16h7M14 8l6 4-6 4z";

    /// <summary>The selection: the triangle inside crop marks.</summary>
    public const string PATH_QUERY_EXECUTE_SELECTION = "M4 8V5h3M17 5h3v3M20 16v3h-3M7 19H4v-3M10 9l5 3-5 3z";

    /// <summary>The canon's plan: three boxes and the lines that join them.</summary>
    public const string PATH_QUERY_PLAN = "M10 3h4a1 1 0 0 1 1 1v2a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1zM4 17h4a1 1 0 0 1 1 1v2a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-2a1 1 0 0 1 1-1zM16 17h4a1 1 0 0 1 1 1v2a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1v-2a1 1 0 0 1 1-1zM12 7v5M6 17v-3h12v3";

    /// <summary>The canon's transaction: two arrows, one each way.</summary>
    public const string PATH_QUERY_TRANSACTION = "M4 8h13l-3-3M20 16H7l3 3";

    // ---------------------------------------------------------------- tabs

    public const string PATH_TAB_CLOSE = PATH_CLOSE;
    public const string PATH_TAB_NEW = PATH_PLUS;
    public const string PATH_TAB_MODIFIED = PATH_COMMON_BULLET;
    public const string PATH_TAB_PIN = "M10 4h4l-.5 7H16a2 2 0 0 1 2 2v1H6v-1a2 2 0 0 1 2-2h2.5zM12 15v5";
    public const string PATH_TAB_UNPIN = "M10 4h4l-.5 7H16a2 2 0 0 1 2 2v1H6v-1a2 2 0 0 1 2-2h2.5zM12 15v5M4 3l16 18";

    // ---------------------------------------------------------------- pagination and direction

    public const string PATH_PAGE_FIRST = "M18 6l-6 6 6 6M7 5v14";
    public const string PATH_PAGE_PREVIOUS = "M15 6l-6 6 6 6";
    public const string PATH_PAGE_NEXT = "M9 6l6 6-6 6";
    public const string PATH_PAGE_LAST = "M6 6l6 6-6 6M17 5v14";

    public const string PATH_ARROW_UP = "M12 19V5M6 11l6-6 6 6";
    public const string PATH_ARROW_DOWN = "M12 5v14M6 13l6 6 6-6";
    public const string PATH_ARROW_LEFT = "M19 12H5M11 6l-6 6 6 6";
    public const string PATH_ARROW_RIGHT = "M5 12h14M13 6l6 6-6 6";

    // ---------------------------------------------------------------- database objects

    /// <summary>The canon's database: an ellipse for the top and two skirts.</summary>
    public const string PATH_DB_DATABASE = "M4 6a8 3 0 1 0 16 0 8 3 0 1 0-16 0M4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3";

    /// <summary>The canon's table: a rounded frame, a header rule and one column rule.</summary>
    public const string PATH_DB_TABLE = "M5 4h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zM3 10h18M9 10v10";

    /// <summary>The canon's view: an eye, because a view is a way of looking at a table.</summary>
    public const string PATH_DB_VIEW = "M2 12s3.6-6 10-6 10 6 10 6-3.6 6-10 6-10-6-10-6zM9.5 12a2.5 2.5 0 1 0 5 0 2.5 2.5 0 1 0-5 0";

    public const string PATH_DB_INDEX = "M4 6h16M4 12h10M4 18h7";
    public const string PATH_DB_TRIGGER = "M13 3L5 13h5l-1 8 8-11h-5z";
    public const string PATH_DB_SEQUENCE = "M4 7h4l2 10h4M14 7h6M17 4l3 3-3 3";
    public const string PATH_DB_ROUTINE = "M6 20V8a3 3 0 0 1 3-3h1M5 12h7M14 10l6 8M20 10l-6 8";

    /// <summary>Primary key. One of the three glyphs section 8.3 lets carry a colour.</summary>
    public const string PATH_DB_KEY = "M4 12a4 4 0 1 0 8 0 4 4 0 1 0-8 0M12 12h9M18 12v4";

    public const string PATH_DB_FOREIGN_KEY = "M10 13a4 4 0 0 0 6 .5l2-2a4 4 0 0 0-6-6l-1 1M14 11a4 4 0 0 0-6-.5l-2 2a4 4 0 0 0 6 6l1-1";
    public const string PATH_DB_LAYERS = "M12 3l9 5-9 5-9-5zM3 13l9 5 9-5";

    // ---------------------------------------------------------------- data

    public const string PATH_COPY = PATH_MENU_COPY;
    public const string PATH_EXPORT = PATH_MENU_EXPORT;
    public const string PATH_COPY_AS_INSERT = "M4 6h11M4 11h11M4 16h6M17 13v8M13 17h8";

    public const string PATH_TABLE_EDITOR_ADD_ROW = PATH_PLUS;
    public const string PATH_TABLE_EDITOR_DELETE_ROW = PATH_TRASH;
    public const string PATH_TABLE_EDITOR_COMMIT = PATH_CHECK;
    public const string PATH_TABLE_EDITOR_ROLLBACK = PATH_UNDO;

    // ---------------------------------------------------------------- workspace tabs

    public const string PATH_TAB_QUERY = PATH_QUERY_EXECUTE;
    public const string PATH_TAB_TABLE_EDIT = PATH_DB_TABLE;
    public const string PATH_TAB_STRUCTURE = PATH_DB_LAYERS;

    // ---------------------------------------------------------------- theme and links

    public const string PATH_THEME_DARK = "M20.5 14.3A8.5 8.5 0 1 1 9.7 3.5a6.6 6.6 0 0 0 10.8 10.8z";
    public const string PATH_THEME_LIGHT = "M9 12a3 3 0 1 0 6 0 3 3 0 1 0-6 0M12 2.5v2.5M12 19v2.5M2.5 12H5M19 12h2.5M5.6 5.6l1.8 1.8M16.6 16.6l1.8 1.8M18.4 5.6l-1.8 1.8M7.4 16.6l-1.8 1.8";

    public const string PATH_LINK_WEB = "M3 12a9 9 0 1 0 18 0 9 9 0 1 0-18 0M3.5 9h17M3.5 15h17M12 3c-2.5 2.4-3.8 5.4-3.8 9s1.3 6.6 3.8 9c2.5-2.4 3.8-5.4 3.8-9S14.5 5.4 12 3z";
    public const string PATH_LINK_GITHUB = "M9 19c-4 1.4-4-2.5-6-3m12 5v-3.6c0-1 .1-1.4-.5-2 2.8-.3 5.5-1.4 5.5-6a4.7 4.7 0 0 0-1.3-3.2 4.3 4.3 0 0 0-.1-3.2S17.5 2.7 15 4.3a11.5 11.5 0 0 0-6 0C6.5 2.7 5.4 3 5.4 3a4.3 4.3 0 0 0-.1 3.2A4.7 4.7 0 0 0 4 9.4c0 4.6 2.7 5.7 5.5 6-.6.6-.6 1.2-.5 2V21";
    public const string PATH_LINK_PERSON = "M8 8a4 4 0 1 0 8 0 4 4 0 1 0-8 0M4 20a8 8 0 0 1 16 0";

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
    public static StreamGeometry COMMON_SEARCH => StreamGeometry.Parse(PATH_COMMON_SEARCH);
    public static StreamGeometry COMMON_BELL => StreamGeometry.Parse(PATH_COMMON_BELL);
    public static StreamGeometry COMMON_WARNING => StreamGeometry.Parse(PATH_COMMON_WARNING);
    public static StreamGeometry COMMON_ERROR => StreamGeometry.Parse(PATH_COMMON_ERROR);
    public static StreamGeometry COMMON_FOLDER => StreamGeometry.Parse(PATH_COMMON_FOLDER);
    public static StreamGeometry COMMON_DELETE => StreamGeometry.Parse(PATH_COMMON_DELETE);
    public static StreamGeometry COMMON_FILTER => StreamGeometry.Parse(PATH_COMMON_FILTER);
    public static StreamGeometry COMMON_COLUMNS => StreamGeometry.Parse(PATH_COMMON_COLUMNS);
    public static StreamGeometry COMMON_GEAR => StreamGeometry.Parse(PATH_COMMON_GEAR);
    public static StreamGeometry COMMON_LOCK => StreamGeometry.Parse(PATH_COMMON_LOCK);

    #endregion

    #region Query Editor

    public static StreamGeometry QUERY_EXECUTE => StreamGeometry.Parse(PATH_QUERY_EXECUTE);
    public static StreamGeometry QUERY_STOP => StreamGeometry.Parse(PATH_QUERY_STOP);
    public static StreamGeometry QUERY_SAVE => StreamGeometry.Parse(PATH_QUERY_SAVE);
    public static StreamGeometry QUERY_CLEAR => StreamGeometry.Parse(PATH_QUERY_CLEAR);
    public static StreamGeometry QUERY_FORMAT => StreamGeometry.Parse(PATH_QUERY_FORMAT);
    public static StreamGeometry QUERY_EXECUTE_SCRIPT => StreamGeometry.Parse(PATH_QUERY_EXECUTE_SCRIPT);
    public static StreamGeometry QUERY_EXECUTE_SELECTION => StreamGeometry.Parse(PATH_QUERY_EXECUTE_SELECTION);
    public static StreamGeometry QUERY_PLAN => StreamGeometry.Parse(PATH_QUERY_PLAN);
    public static StreamGeometry QUERY_TRANSACTION => StreamGeometry.Parse(PATH_QUERY_TRANSACTION);
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
    public static StreamGeometry CHEVRON_RIGHT => StreamGeometry.Parse(PATH_PAGE_NEXT);
    public static StreamGeometry CHEVRON_LEFT => StreamGeometry.Parse(PATH_PAGE_PREVIOUS);

    #endregion

    #region Database Objects

    public static StreamGeometry DB_DATABASE => StreamGeometry.Parse(PATH_DB_DATABASE);
    public static StreamGeometry DB_TABLE => StreamGeometry.Parse(PATH_DB_TABLE);
    public static StreamGeometry DB_VIEW => StreamGeometry.Parse(PATH_DB_VIEW);
    public static StreamGeometry DB_INDEX => StreamGeometry.Parse(PATH_DB_INDEX);
    public static StreamGeometry DB_TRIGGER => StreamGeometry.Parse(PATH_DB_TRIGGER);
    public static StreamGeometry DB_SEQUENCE => StreamGeometry.Parse(PATH_DB_SEQUENCE);
    public static StreamGeometry DB_ROUTINE => StreamGeometry.Parse(PATH_DB_ROUTINE);
    public static StreamGeometry DB_KEY => StreamGeometry.Parse(PATH_DB_KEY);
    public static StreamGeometry DB_FOREIGN_KEY => StreamGeometry.Parse(PATH_DB_FOREIGN_KEY);
    public static StreamGeometry DB_LAYERS => StreamGeometry.Parse(PATH_DB_LAYERS);

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

    #region Theme

    public static StreamGeometry THEME_DARK => StreamGeometry.Parse(PATH_THEME_DARK);
    public static StreamGeometry THEME_LIGHT => StreamGeometry.Parse(PATH_THEME_LIGHT);

    #endregion

    #region Links

    public static StreamGeometry LINK_WEB => StreamGeometry.Parse(PATH_LINK_WEB);
    public static StreamGeometry LINK_GITHUB => StreamGeometry.Parse(PATH_LINK_GITHUB);
    public static StreamGeometry LINK_PERSON => StreamGeometry.Parse(PATH_LINK_PERSON);

    #endregion
}
