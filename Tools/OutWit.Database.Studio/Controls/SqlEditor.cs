using Avalonia;
using Avalonia.Data;
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
        FontFamily = new FontFamily("Consolas, Courier New, monospace");
        FontSize = 13;
        ShowLineNumbers = true;
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        WordWrap = false;
        Padding = new Thickness(4);

        SyntaxHighlighting = WitSqlHighlighting.Definition;

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
        TextArea.SelectionChanged += OnSelectionChanged;
        
        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += OnThemeChanged;
        }
    }

    #endregion

    #region Functions

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyThemeColors();
    }

    /// <summary>
    /// TextEditor has no automation peer of its own, so without this the editor is not an element at
    /// all - invisible to screen readers, and unreachable by UI automation, which is what made the
    /// application impossible to drive end to end. See <see cref="SqlEditorAutomationPeer"/>.
    /// </summary>
    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
    {
        return new SqlEditorAutomationPeer(this);
    }

    private void ApplyThemeColors()
    {
        var bgColor = SqlEditorTheme.BackgroundColor;
        var fgColor = SqlEditorTheme.ForegroundColor;
        var lnColor = SqlEditorTheme.LineNumbersColor;

        // Use LocalValue priority to override styles
        SetValue(BackgroundProperty, new SolidColorBrush(bgColor), BindingPriority.LocalValue);
        SetValue(ForegroundProperty, new SolidColorBrush(fgColor), BindingPriority.LocalValue);
        LineNumbersForeground = new SolidColorBrush(lnColor);

        // Force redraw
        InvalidateVisual();
        TextArea.TextView.Redraw();
    }

    /// <summary>
    /// Refreshes theme colors. Call this after theme change.
    /// </summary>
    public void RefreshTheme()
    {
        ApplyThemeColors();
        SyntaxHighlighting = WitSqlHighlighting.CreateDefinition();
    }

    private void UpdateSelectedText()
    {
        var selection = TextArea.Selection;
        if (selection.IsEmpty)
        {
            SelectedText = null;
        }
        else
        {
            SelectedText = selection.GetText();
        }
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

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        UpdateSelectedText();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        RefreshTheme();
    }

    #endregion

    #region Cleanup

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        TextArea.SelectionChanged -= OnSelectionChanged;

        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged -= OnThemeChanged;
        }
    }

    #endregion

    #region Properties

    [StyledProperty]
    public string? SqlText { get; set; }

    [StyledProperty]
    public new string? SelectedText { get; set; }

    protected override Type StyleKeyOverride => typeof(TextEditor);

    #endregion
}
