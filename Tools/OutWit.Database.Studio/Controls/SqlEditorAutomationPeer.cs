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
