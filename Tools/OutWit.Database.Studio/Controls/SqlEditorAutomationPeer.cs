using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace OutWit.Database.Studio.Controls;

/// <summary>
/// Makes the SQL editor visible to screen readers and to UI automation (S8).
///
/// AvaloniaEdit's TextEditor ships no automation peer, so the editor was not an element at all: a
/// screen reader could not read or edit the query, and UI automation could not type into it - text
/// sent to the focused element simply went nowhere. That is an accessibility defect on its own, and
/// it is also why the redesign ahead had no way to be verified except by looking at screenshots.
///
/// IValueProvider is the pattern a single-line-or-multiline text control exposes: Value reads the
/// query, SetValue replaces it. That is exactly what an automated run of "open a database, type a
/// query, execute it" needs.
/// </summary>
public class SqlEditorAutomationPeer : ControlAutomationPeer, IValueProvider
{
    #region Constructors

    public SqlEditorAutomationPeer(SqlEditor owner)
        : base(owner)
    {
    }

    #endregion

    #region ControlAutomationPeer

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;

    protected override string GetClassNameCore() => nameof(SqlEditor);

    /// <summary>
    /// Falls back to a name of its own. Without one the editor announces as its class, which is the
    /// same unhelpful answer most of Studio's buttons used to give.
    /// </summary>
    protected override string? GetNameCore()
    {
        var name = base.GetNameCore();

        return string.IsNullOrEmpty(name) ? "SQL editor" : name;
    }

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    /// <summary>
    /// Where the caret is and what is wrong with the text, spoken (S10).
    ///
    /// The right pattern for this is <c>ITextProvider</c>, and <b>Avalonia 12 does not have one</b> -
    /// its automation surface is IValue, IRange, IToggle, ISelection, IInvoke, IExpandCollapse and
    /// IScroll, and nothing about text ranges. So the caret and the error cannot be exposed as
    /// structure; help text is what a screen reader will read out, and it carries both.
    ///
    /// This is a smaller answer than the design asks for and it is the whole one available here.
    /// </summary>
    protected override string? GetHelpTextCore()
    {
        if (Owner is not SqlEditor editor)
            return base.GetHelpTextCore();

        var where = $"line {editor.CaretLine}, column {editor.CaretColumn}";

        return editor.UnderlineLine > 0
            ? $"{where}. An error is marked on line {editor.UnderlineLine}."
            : where;
    }

    /// <summary>
    /// Tells whoever is listening that the text or the caret moved. Without this a screen reader reads
    /// the editor once and never again - which is what "has an automation peer" quietly meant before.
    /// </summary>
    public void NotifyTextChanged(string? oldValue, string? newValue)
    {
        RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, oldValue, newValue);
    }

    public void NotifyCaretMoved()
    {
        RaisePropertyChangedEvent(AutomationElementIdentifiers.HelpTextProperty, null, GetHelpTextCore());
    }

    #endregion

    #region IValueProvider

    public bool IsReadOnly => Owner is SqlEditor { IsReadOnly: true };

    public string? Value => (Owner as SqlEditor)?.Text;

    public void SetValue(string? value)
    {
        if (Owner is not SqlEditor editor)
            return;

        if (editor.IsReadOnly)
            throw new InvalidOperationException("The SQL editor is read-only.");

        editor.Text = value ?? string.Empty;
    }

    #endregion
}
