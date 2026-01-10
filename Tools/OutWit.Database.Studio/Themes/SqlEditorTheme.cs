using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace OutWit.Database.Studio.Themes;

/// <summary>
/// Provides SQL Editor theme colors from application resources.
/// Editor background/foreground colors are defined here.
/// Syntax highlighting colors are defined in xshd files (WitSql.xshd / WitSqlLight.xshd).
/// </summary>
public static class SqlEditorTheme
{
    #region Constants

    // Resource keys for editor colors
    private const string BACKGROUND_COLOR_KEY = "SqlEditorBackgroundColor";
    private const string FOREGROUND_COLOR_KEY = "SqlEditorForegroundColor";
    private const string LINE_NUMBERS_COLOR_KEY = "SqlEditorLineNumbersColor";

    // Default colors (light theme fallback)
    private static readonly Color DEFAULT_BACKGROUND_LIGHT = Color.Parse("#FFFFFF");
    private static readonly Color DEFAULT_FOREGROUND_LIGHT = Color.Parse("#1E1E1E");
    private static readonly Color DEFAULT_LINE_NUMBERS_LIGHT = Color.Parse("#6E6E6E");

    // Default colors (dark theme fallback)
    private static readonly Color DEFAULT_BACKGROUND_DARK = Color.Parse("#1E1E1E");
    private static readonly Color DEFAULT_FOREGROUND_DARK = Color.Parse("#D4D4D4");
    private static readonly Color DEFAULT_LINE_NUMBERS_DARK = Color.Parse("#858585");

    #endregion

    #region Functions

    /// <summary>
    /// Gets a color from application resources by key.
    /// </summary>
    /// <param name="key">Resource key.</param>
    /// <param name="defaultColor">Fallback color if resource not found.</param>
    /// <returns>Color from resources or default.</returns>
    public static Color GetColor(string key, Color defaultColor)
    {
        if (Application.Current?.Resources.TryGetResource(key, Application.Current.ActualThemeVariant, out var resource) == true)
        {
            if (resource is Color color)
                return color;
        }

        return defaultColor;
    }

    /// <summary>
    /// Gets a SolidColorBrush for the specified color.
    /// </summary>
    public static SolidColorBrush GetBrush(Color color)
    {
        return new SolidColorBrush(color);
    }

    /// <summary>
    /// Determines if the current theme is dark.
    /// </summary>
    public static bool IsDarkTheme()
    {
        if (Application.Current == null)
            return false;

        return Application.Current.ActualThemeVariant == ThemeVariant.Dark;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the editor background color for the current theme.
    /// </summary>
    public static Color BackgroundColor
    {
        get
        {
            var defaultColor = IsDarkTheme() ? DEFAULT_BACKGROUND_DARK : DEFAULT_BACKGROUND_LIGHT;
            return GetColor(BACKGROUND_COLOR_KEY, defaultColor);
        }
    }

    /// <summary>
    /// Gets the editor foreground color for the current theme.
    /// </summary>
    public static Color ForegroundColor
    {
        get
        {
            var defaultColor = IsDarkTheme() ? DEFAULT_FOREGROUND_DARK : DEFAULT_FOREGROUND_LIGHT;
            return GetColor(FOREGROUND_COLOR_KEY, defaultColor);
        }
    }

    /// <summary>
    /// Gets the line numbers color for the current theme.
    /// </summary>
    public static Color LineNumbersColor
    {
        get
        {
            var defaultColor = IsDarkTheme() ? DEFAULT_LINE_NUMBERS_DARK : DEFAULT_LINE_NUMBERS_LIGHT;
            return GetColor(LINE_NUMBERS_COLOR_KEY, defaultColor);
        }
    }

    /// <summary>
    /// Gets the editor background brush.
    /// </summary>
    public static SolidColorBrush BackgroundBrush => GetBrush(BackgroundColor);

    /// <summary>
    /// Gets the editor foreground brush.
    /// </summary>
    public static SolidColorBrush ForegroundBrush => GetBrush(ForegroundColor);

    /// <summary>
    /// Gets the line numbers brush.
    /// </summary>
    public static SolidColorBrush LineNumbersBrush => GetBrush(LineNumbersColor);

    #endregion
}
