using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using OutWit.Common.Locker;
using OutWit.Common.MVVM.Attributes;
using OutWit.Database.Studio.Syntax;

namespace OutWit.Database.Studio.Controls;

/// <summary>
/// SQL Editor control with syntax highlighting based on AvaloniaEdit.
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
        Background = new SolidColorBrush(Color.Parse("#1E1E1E"));
        Foreground = new SolidColorBrush(Color.Parse("#D4D4D4"));
        LineNumbersForeground = new SolidColorBrush(Color.Parse("#858585"));
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        WordWrap = false;
        Padding = new Thickness(4);

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
    }

    #endregion

    #region Event Handlers

    private void OnSqlTextPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if(GlobalLocker.IsLocked(nameof(SqlEditor)))
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
