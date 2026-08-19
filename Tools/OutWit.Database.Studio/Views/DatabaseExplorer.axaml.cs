using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Views;

/// <summary>
/// Database explorer tree view control.
/// </summary>
public partial class DatabaseExplorer : UserControl
{
    #region Constants

    /// <summary>How long a typed prefix lives before the next letter starts a new search.</summary>
    private static readonly TimeSpan TYPE_AHEAD_PAUSE = TimeSpan.FromSeconds(1);

    #endregion

    #region Fields

    private string m_typed = string.Empty;
    private DateTime m_typedAt = DateTime.MinValue;

    /// <summary>The row a double click has just opened the data of, and the state it was in.</summary>
    private TreeViewItem? m_rowToLeaveAsItWas;
    private bool m_asItWas;

    #endregion

    #region Static

    /// <summary>
    /// Tells the node that its row has been opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Measured in the running application, 2026-08-19.</b> The tree binds a row's
    /// <c>IsExpanded</c> to the node's in a STYLE setter, and a binding in a style setter does not
    /// push back: the row opened, the model never heard, and the columns of a table were never
    /// read. Nothing had noticed because until the placeholder child arrived there was no expander
    /// to press, and the tests set <c>IsExpanded</c> on the node themselves - which is the
    /// ViewModel's side of a binding that only ever worked one way.
    /// </para>
    /// <para>
    /// A class handler rather than a per-item subscription: the containers are recycled as the
    /// tree scrolls, so subscribing when one is prepared means unsubscribing when it is not.
    /// </para>
    /// </remarks>
    static DatabaseExplorer()
    {
        TreeViewItem.IsExpandedProperty.Changed.AddClassHandler<TreeViewItem>((item, e) =>
        {
            if (item.DataContext is DatabaseNode node && e.GetNewValue<bool>())
                node.IsExpanded = true;
        });
    }

    #endregion
    #region Constructors

    public DatabaseExplorer()
    {
        InitializeComponent();
        DataContext = ApplicationViewModel.Instance;

        KeyDown += OnKeyDown;

        // Tunnelling, and it carries BOTH the middle click and the double click: a TreeViewItem
        // handles the pointer for its own selection and for its own expansion, and a bubbling
        // handler sees neither.
        //
        // The double click USED to be read from DoubleTapped, which is where it belongs - until a
        // table gained a placeholder child, the row began toggling on a double tap, and the tree
        // marked the event handled before this control saw it. That was repaired by asking for the
        // TUNNELLING route of DoubleTapped, and there is no such route: the event is registered
        // Bubble alone, so the handler was never called again and opening a table's data did
        // nothing at all - through a green suite, a green CI and a signed release. The pointer is
        // the event that tunnels, so the double click is read from it. See
        // AHandlerRunsOnlyOnARouteItsEventTravelsTests.
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);

        // And the second half of the same click. Handling the pointer press does NOT stop the
        // double tap: the gesture is recognised from the finished route, so the row still toggled
        // itself and a table both opened its data and opened its row. Measured on 2026-08-19. This
        // runs after the row has done that - bubbling, and handledEventsToo because the row marks
        // the tap handled - and puts the row back the way it was.
        AddHandler(DoubleTappedEvent, OnDoubleTappedAfterTheRow, RoutingStrategies.Bubble,
            handledEventsToo: true);

        // BUBBLING, and deliberately not tunnelling: the filter box and the rename box are text
        // boxes, and a tunnelling handler would eat every letter typed into them and jump the tree
        // instead. A box that has taken the character marks the event handled, and a bubbling
        // handler is then never called - which is exactly the rule wanted here.
        TextInput += OnTextInput;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// A double click opens the thing the node IS - a table's data rather than its structure
    /// (WS-19), a view's rows, and a connection's own tab.
    ///
    /// It used to open the structure, with the data hidden in the context menu - while looking at the
    /// data is what people come to a database tool to do, by an order of magnitude. The structure is
    /// on F4 and in the menu.
    ///
    /// <para>
    /// That last sentence used to say "Ctrl+Enter" and was FALSE for as long as it existed. Phase 17
    /// pressed the key: nothing happened, because the shell binds Ctrl+Return to "run the selection"
    /// and nothing anywhere bound the structure to anything. A comment is the author's claim, not the
    /// application's behaviour.
    /// </para>
    /// </summary>
    private void OpenWhatIsUnderThePointer(PointerPressedEventArgs e)
    {
        // The chevron is a control of its own, and two clicks on it are two toggles rather than a
        // request for what is in the row.
        if (PressedOnTheChevron(e))
            return;

        if (ItemUnder(e) is not { } item || item.DataContext is not DatabaseNode node)
            return;

        var explorer = ApplicationViewModel.Instance.DatabaseExplorerVm;

        // The row under the pointer, not the row that happens to be selected. The first click of
        // the pair selects it anyway; saying so here is what makes the handler independent of the
        // order the tree does its own work in.
        explorer.SelectedNode = node;

        // WHAT is opened is the ViewModel's decision, not this handler's: a gesture belongs here, a
        // rule does not, and a rule written here is a rule no test can read.
        if (!explorer.CanOpenWhatItIs)
            return;

        explorer.OpenWhatItIsCommand.Execute(null);

        // The tap that follows this press will toggle the row. Remember what to put back.
        m_rowToLeaveAsItWas = item;
        m_asItWas = item.IsExpanded;

        e.Handled = true;
    }

    /// <summary>
    /// A double click on a table opens its data and does nothing else - in particular it does not
    /// open the row, which the tree does on its own and which nothing here can prevent.
    /// </summary>
    /// <remarks>
    /// The chevron and the arrow keys are how a row is opened, and they are unaffected: this puts
    /// back only the row a double click has just opened the data of.
    /// </remarks>
    private void OnDoubleTappedAfterTheRow(object? sender, TappedEventArgs e)
    {
        if (m_rowToLeaveAsItWas is not { } row)
            return;

        m_rowToLeaveAsItWas = null;

        row.IsExpanded = m_asItWas;
    }

    /// <summary>
    /// The middle click opens the object's data in a tab that does NOT come to the front (2.7).
    ///
    /// <para>
    /// <b>The middle button does not select</b>, so the node cannot be read from
    /// <c>SelectedNode</c> - it is taken from the item under the pointer, and the selection is moved
    /// there deliberately, because the canon says a click selects and this is a click. Without that
    /// the inspector on the right would keep describing a different object from the one whose data
    /// just opened.
    /// </para>
    /// </summary>
    private void OpenInTheBackground(PointerPressedEventArgs e)
    {
        if (NodeUnder(e) is not { } node)
            return;

        var explorer = ApplicationViewModel.Instance.DatabaseExplorerVm;

        explorer.SelectedNode = node;

        if (!explorer.CanBrowseData)
            return;

        explorer.BrowseDataInBackground();
        e.Handled = true;
    }

    /// <summary>
    /// The two clicks the TREE owns, both read from the pointer and both before the row sees them.
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;

        if (properties.IsMiddleButtonPressed)
        {
            OpenInTheBackground(e);
            return;
        }

        if (properties.IsLeftButtonPressed && e.ClickCount == 2)
            OpenWhatIsUnderThePointer(e);
    }

    /// <summary>The row the pointer is over, if it is over one.</summary>
    private static TreeViewItem? ItemUnder(PointerPressedEventArgs e)
    {
        return (e.Source as Visual)?
            .GetSelfAndVisualAncestors()
            .OfType<TreeViewItem>()
            .FirstOrDefault();
    }

    /// <summary>The node whose row the pointer is over, if it is over one.</summary>
    private static DatabaseNode? NodeUnder(PointerPressedEventArgs e)
    {
        return ItemUnder(e)?.DataContext as DatabaseNode;
    }

    /// <summary>Whether the press landed on the row's expander rather than on the row.</summary>
    private static bool PressedOnTheChevron(PointerPressedEventArgs e)
    {
        return (e.Source as Visual)?
            .GetSelfAndVisualAncestors()
            .TakeWhile(visual => visual is not TreeViewItem)
            .OfType<Button>()
            .Any() == true;
    }

    /// <summary>
    /// The keys the TREE owns (2.7). They sit here rather than on the shell for stage 4's reason: a
    /// <c>KeyBinding</c> on the window needs the event to bubble from a FOCUSED element, and the tree
    /// is one - the shell is not.
    ///
    /// <list type="bullet">
    /// <item><c>F2</c> renames, deferred here from stage 5.</item>
    /// <item><c>F4</c> opens the structure. <b>WS-19 says «структура — Ctrl+Enter» and that was never
    /// true</b>: the shell binds <c>Ctrl+Return</c> to "run the selection", the only
    /// <c>InputGesture="Ctrl+Enter"</c> in the application is that menu item's LABEL, and the
    /// keyboard map had no entry for the structure at all. Measured by pressing it - the tab list did
    /// not change.
    ///
    /// <para>
    /// <b><c>Alt+Enter</c> was chosen first and MEASURED NOT TO WORK, which is worth keeping.</b> It
    /// is what Windows means by "properties of the selected object", and the reasoning for it was
    /// that the menu's access keys are LETTERS - the English menu claims fifteen and the Russian menu
    /// claims fifteen different ones - so a non-letter looked safe. It is not: Avalonia's
    /// <c>Menu</c> takes <c>Alt</c> ITSELF to enter access-key mode. Driven with the focus provably
    /// in the tree (the arrow keys moved the selection first), <c>Alt+Enter</c> opened nothing and
    /// lit the underlines in the menu bar. <b>No <c>Alt</c> gesture can reach a control while this
    /// window has a menu bar.</b>
    /// </para>
    /// <para>
    /// <c>F4</c> is the other Windows convention for properties - Visual Studio's - it is one key,
    /// the menu cannot take it, and it was free.
    /// </para></item>
    /// <item><c>Delete</c> drops, through the same question the context menu asks.</item>
    /// </list>
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var explorer = ApplicationViewModel.Instance.DatabaseExplorerVm;

        switch (e.Key)
        {
            case Key.F2 when explorer.CanRename:
                explorer.BeginRenameCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F4 when explorer.CanViewStructure:
                explorer.ViewStructureCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Delete when explorer.CanDropObject:
                explorer.DropObjectCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Type-ahead (2.7): letters walk the selection to the first open node whose name starts with
    /// them. The matching itself is <see cref="DatabaseExplorerViewModel.JumpTo"/>, so it can be
    /// driven without a window; what belongs here is the buffer.
    /// </summary>
    /// <remarks>
    /// The pause is what turns two words into two searches. Without it "ac" typed a minute apart is
    /// one prefix that matches nothing, and the tree stops answering the keyboard with no sign why.
    /// </remarks>
    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        var text = e.Text;

        if (string.IsNullOrEmpty(text) || char.IsControl(text[0]))
            return;

        // A space can be INSIDE a name but never starts a search - and space on a tree is the key a
        // person presses to toggle something, not to type.
        if (m_typed.Length == 0 && char.IsWhiteSpace(text[0]))
            return;

        var now = DateTime.UtcNow;

        m_typed = now - m_typedAt > TYPE_AHEAD_PAUSE
            ? text
            : m_typed + text;

        m_typedAt = now;

        if (ApplicationViewModel.Instance.DatabaseExplorerVm.JumpTo(m_typed))
            e.Handled = true;
    }

    /// <summary>
    /// Enter renames, Escape puts the old name back. Both close the box.
    /// </summary>
    private void RenameBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        var explorer = ApplicationViewModel.Instance.DatabaseExplorerVm;

        switch (e.Key)
        {
            case Avalonia.Input.Key.Enter:
                explorer.CommitRenameCommand.Execute(null);
                e.Handled = true;
                break;

            case Avalonia.Input.Key.Escape:
                explorer.CancelRenameCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// The caret goes into the box as it appears. Stage 4 found this the hard way with the command
    /// palette: a box that is visible and not focused is a box the user has to click.
    /// </summary>
    private void RenameBox_Attached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        box.Focus();
        box.SelectAll();
    }

    #endregion
}
