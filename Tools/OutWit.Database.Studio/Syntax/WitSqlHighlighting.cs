using System.Reflection;
using System.Xml;
using Avalonia;
using Avalonia.Styling;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace OutWit.Database.Studio.Syntax;

/// <summary>
/// Provides WitSQL syntax highlighting for AvaloniaEdit.
/// Supports light and dark themes through separate xshd files.
/// </summary>
public static class WitSqlHighlighting
{
    #region Constants

    private const string RESOURCE_NAME_DARK = "OutWit.Database.Studio.Syntax.WitSql.xshd";
    private const string RESOURCE_NAME_LIGHT = "OutWit.Database.Studio.Syntax.WitSqlLight.xshd";

    #endregion

    #region Fields

    private static IHighlightingDefinition? m_definition;
    private static bool m_isDarkTheme;
    private static readonly Lock LOCK = new();

    #endregion

    #region Properties

    /// <summary>
    /// Gets the WitSQL syntax highlighting definition for the current theme.
    /// </summary>
    public static IHighlightingDefinition Definition
    {
        get
        {
            var isDark = IsDarkTheme();
            
            lock (LOCK)
            {
                if (m_definition == null || m_isDarkTheme != isDark)
                {
                    m_isDarkTheme = isDark;
                    m_definition = LoadDefinition(isDark);
                }
            }

            return m_definition;
        }
    }

    #endregion

    #region Functions

    /// <summary>
    /// Creates a new highlighting definition for the current theme.
    /// Call this when theme changes to refresh colors.
    /// </summary>
    public static IHighlightingDefinition CreateDefinition()
    {
        InvalidateCache();
        return Definition;
    }

    private static IHighlightingDefinition LoadDefinition(bool isDarkTheme)
    {
        var resourceName = isDarkTheme ? RESOURCE_NAME_DARK : RESOURCE_NAME_LIGHT;
        var assembly = Assembly.GetExecutingAssembly();
        
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException($"Embedded resource not found: {resourceName}");

        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private static bool IsDarkTheme()
    {
        if (Application.Current == null)
            return false;

        var themeVariant = Application.Current.ActualThemeVariant;
        return themeVariant == ThemeVariant.Dark;
    }

    /// <summary>
    /// Registers WitSQL highlighting with the global HighlightingManager.
    /// </summary>
    public static void Register()
    {
        HighlightingManager.Instance.RegisterHighlighting(
            "WitSQL",
            [".sql", ".witsql"],
            Definition);
    }

    /// <summary>
    /// Clears the cached definition to force recreation on next access.
    /// Call this when theme changes.
    /// </summary>
    public static void InvalidateCache()
    {
        lock (LOCK)
        {
            m_definition = null;
        }
    }

    #endregion
}
