using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using OutWit.Common.Locker;
using OutWit.Common.MVVM.Attributes;
using OutWit.Database.Studio.Syntax;
using OutWit.Database.Studio.Themes;

namespace OutWit.Database.Studio.Controls;

/// <summary>
/// SQL Editor control with syntax highlighting based on AvaloniaEdit.
/// Supports theme-aware colors through application resources.
/// </summary>
public partial class SqlEditor : TextEditor
{
    #region Static

    static SqlEditor()
    {
        SqlTextProperty.Changed.AddClassHandler<SqlEditor>((editor, e) => editor.OnSqlTextPropertyChanged(e));
    }

    #endregion

    #region Constructors

    public SqlEditor()
    {
        InitDefaults();
        InitEvents();
    }

    #endregion

    #region Initialization

    private void InitDefaults()
    {
        // Set up appearance
        FontFamily = new FontFamily("Consolas, Courier New, monospace");
        FontSize = 13;
        ShowLineNumbers = true;
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        WordWrap = false;
        Padding = new Thickness(4);

        // Apply theme-aware colors
        ApplyThemeColors();

        // Apply WitSQL syntax highlighting
        SyntaxHighlighting = WitSqlHighlighting.Definition;

        // Configure editor options
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.ConvertTabsToSpaces = true;
        Options.IndentationSize = 4;
        Options.ShowSpaces = false;
        Options.ShowTabs = false;
        Options.HighlightCurrentLine = true;
    }

    private void InitEvents()
    {
        TextChanged += OnEditorTextChanged;
        
        // Subscribe to theme changes
        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += OnThemeChanged;
        }
    }

    #endregion

    #region Functions

    private void ApplyThemeColors()
    {
        Background = SqlEditorTheme.BackgroundBrush;
        Foreground = SqlEditorTheme.ForegroundBrush;
        LineNumbersForeground = SqlEditorTheme.LineNumbersBrush;
    }

    #endregion

    #region Event Handlers

    private void OnSqlTextPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (GlobalLocker.IsLocked(nameof(SqlEditor)))
            return;

        using var locker = GlobalLocker.Lock(nameof(SqlEditor));

        Text = e.NewValue as string ?? string.Empty;
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (GlobalLocker.IsLocked(nameof(SqlEditor)))
            return;

        using var locker = GlobalLocker.Lock(nameof(SqlEditor));

        SqlText = Text;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        // Re-apply colors when theme changes
        ApplyThemeColors();
        
        // Re-apply syntax highlighting to refresh colors
        SyntaxHighlighting = WitSqlHighlighting.CreateDefinition();
    }

    #endregion

    #region Cleanup

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged -= OnThemeChanged;
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the SQL text content (bindable).
    /// </summary>
    [StyledProperty]
    public string? SqlText { get; set; }

    protected override Type StyleKeyOverride => typeof(TextEditor);

    #endregion
}
