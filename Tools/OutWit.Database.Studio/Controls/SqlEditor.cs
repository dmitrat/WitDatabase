using Avalonia;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using OutWit.Common.Locker;
using OutWit.Common.MVVM.Attributes;
using OutWit.Database.Studio.Services;
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

        UnderlineLineProperty.Changed.AddClassHandler<SqlEditor>((editor, _) => editor.RefreshUnderline());
        UnderlineColumnProperty.Changed.AddClassHandler<SqlEditor>((editor, _) => editor.RefreshUnderline());
        UnderlineLengthProperty.Changed.AddClassHandler<SqlEditor>((editor, _) => editor.RefreshUnderline());
    }

    #endregion

    #region Fields

    private readonly SqlErrorUnderline m_underline = new();
    private CompletionWindow? m_completion;
    private SqlEditorAutomationPeer? m_peer;

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
        TextArea.Caret.PositionChanged += OnCaretChanged;
        TextArea.TextEntered += OnTextEntered;
        TextArea.KeyDown += OnTextAreaKeyDown;

        TextArea.TextView.BackgroundRenderers.Add(m_underline);

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
        return m_peer = new SqlEditorAutomationPeer(this);
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

        var previous = SqlText;

        SqlText = Text;

        m_peer?.NotifyTextChanged(previous, Text);
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        UpdateSelectedText();
    }

    /// <summary>
    /// Where the caret is, for the status bar and for F5.
    ///
    /// F5 runs the statement the cursor is in (WS-25), and the only place that knows where the cursor
    /// is, is the editor. Line and column are reported as a person counts them - both from 1 - which
    /// is what the status bar shows; the offset is what the statement lookup uses.
    /// </summary>
    private void OnCaretChanged(object? sender, EventArgs e)
    {
        CaretOffsetInText = CaretOffset;
        CaretLine = TextArea.Caret.Line;
        CaretColumn = TextArea.Caret.Column;

        m_peer?.NotifyCaretMoved();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        RefreshTheme();
    }

    /// <summary>
    /// Completion opens by itself after a dot, and on request everywhere else (WS-24, 3.2).
    ///
    /// Automatically after a letter as well would put a list in front of somebody typing a string
    /// literal or a name the schema has never heard of; after a dot there is exactly one useful answer
    /// and it is worth offering unasked.
    /// </summary>
    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (e.Text == ".")
            _ = ShowCompletionAsync();
    }

    private void OnTextAreaKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            _ = ShowCompletionAsync();
        }
    }

    #endregion

    #region Completion

    /// <summary>
    /// Asks the source what belongs at the caret and shows it. Everything about WHAT to offer is the
    /// source's; everything here is about a window.
    /// </summary>
    public async Task ShowCompletionAsync()
    {
        var source = CompletionSource;

        if (source == null)
            return;

        var caret = CaretOffset;
        var text = Text;

        IReadOnlyList<SqlCompletionItem> items;

        try
        {
            items = await source.SuggestAsync(text, caret);
        }
        catch (Exception)
        {
            // Completion is a convenience over an editor. It never gets to break typing.
            return;
        }

        if (items.Count == 0 || CaretOffset != caret || !ReferenceEquals(text, Text) && Text != text)
            return;

        var replaceFrom = source.CompletionStart(text, caret);

        m_completion?.Close();

        var window = new CompletionWindow(TextArea)
        {
            CloseAutomatically = true,
            CloseWhenCaretAtBeginning = true
        };

        // The order is the source's - it knows what the caret is in front of - so the priorities go
        // down the list rather than being recomputed by a window that knows less.
        var priority = items.Count;

        foreach (var item in items)
            window.CompletionList.CompletionData.Add(new SqlCompletionData(item, replaceFrom) { Priority = priority-- });

        window.StartOffset = replaceFrom;
        window.EndOffset = caret;

        window.Closed += (_, _) => m_completion = null;

        m_completion = window;
        window.Show();
    }

    #endregion

    #region Underline

    private void RefreshUnderline()
    {
        m_underline.Line = UnderlineLine;
        m_underline.Column = UnderlineColumn;
        m_underline.Length = UnderlineLength;

        TextArea.TextView.InvalidateLayer(m_underline.Layer);
    }

    #endregion

    #region Cleanup

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        TextArea.SelectionChanged -= OnSelectionChanged;
        TextArea.Caret.PositionChanged -= OnCaretChanged;
        TextArea.TextEntered -= OnTextEntered;
        TextArea.KeyDown -= OnTextAreaKeyDown;

        m_completion?.Close();

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

    /// <summary>
    /// The caret's position in the text, in characters. Named apart from AvaloniaEdit's own
    /// CaretOffset, which is not a styled property and cannot be bound.
    /// </summary>
    [StyledProperty]
    public int CaretOffsetInText { get; set; }

    [StyledProperty]
    public int CaretLine { get; set; }

    [StyledProperty]
    public int CaretColumn { get; set; }

    /// <summary>
    /// Where the wavy line goes: 1-based line, 0-based column, and how many characters. Line 0 means
    /// there is nothing wrong.
    /// </summary>
    [StyledProperty]
    public int UnderlineLine { get; set; }

    [StyledProperty]
    public int UnderlineColumn { get; set; }

    [StyledProperty]
    public int UnderlineLength { get; set; }

    /// <summary>
    /// Who decides what to suggest at the caret. The query tab, in practice - and nothing about the
    /// deciding lives in this control, which is what lets completion be tested without a window.
    /// </summary>
    [StyledProperty]
    public ISqlCompletionSource? CompletionSource { get; set; }

    protected override Type StyleKeyOverride => typeof(TextEditor);

    #endregion
}
