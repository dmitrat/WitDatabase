using System.Runtime.InteropServices;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Where a shortcut works, which is the difference between "it does nothing" and "it does nothing
/// HERE".
/// </summary>
public enum KeyboardScope
{
    /// <summary>Anywhere in the window.</summary>
    Window,

    /// <summary>Only while a query tab is in front.</summary>
    Query,

    /// <summary>Only while the focus is in a result grid.</summary>
    Grid,

    /// <summary>Only while the focus is in the object tree.</summary>
    Explorer
}

/// <summary>One row of the keyboard map (9.6).</summary>
/// <param name="ActionKey">Catalogue key for what it does.</param>
/// <param name="Gesture">The gesture as this application writes it: <c>Ctrl+Shift+F5</c>.</param>
/// <param name="Scope">Where it works.</param>
public sealed record KeyboardShortcut(string ActionKey, string Gesture, KeyboardScope Scope);

/// <summary>
/// Every keyboard shortcut Studio has, as data (9.6, WS-69).
/// </summary>
/// <remarks>
/// <para>
/// <b>A list of shortcuts is a description, and a description drifts.</b> This one is checked against
/// the application in both directions by <c>KeyboardMapTests</c>: every gesture here has to be bound
/// somewhere that really handles it, and every binding in the markup has to be here. A help window
/// that quietly disagrees with the keys is worse than no help window - it is the one place a person
/// goes when a key did not work.
/// </para>
/// <para>
/// <b>The list is the same object the window shows and the tests walk</b> - the shape
/// <c>SchemaCapabilities</c> and <c>StorageCapabilities</c> already use here, and for the same reason.
/// </para>
/// </remarks>
public static class KeyboardMap
{
    #region Constants

    /// <summary>
    /// The map, in the order the window shows it: what a person reaches for most, first.
    /// </summary>
    public static IReadOnlyList<KeyboardShortcut> Shortcuts { get; } =
    [
        new("Keys.Palette", "Ctrl+K", KeyboardScope.Window),
        new("Keys.NewDatabase", "Ctrl+N", KeyboardScope.Window),
        new("Keys.OpenDatabase", "Ctrl+O", KeyboardScope.Window),
        new("Keys.Refresh", "Ctrl+R", KeyboardScope.Window),
        new("Keys.Help", "Ctrl+?", KeyboardScope.Window),

        new("Keys.NewQueryTab", "Ctrl+T", KeyboardScope.Window),
        new("Keys.ReopenTab", "Ctrl+Shift+T", KeyboardScope.Window),
        new("Keys.CloseTab", "Ctrl+W", KeyboardScope.Window),
        new("Keys.Save", "Ctrl+S", KeyboardScope.Window),
        new("Keys.SaveAs", "Ctrl+Shift+S", KeyboardScope.Window),

        new("Keys.ExecuteStatement", "F5", KeyboardScope.Query),
        new("Keys.ExecuteScript", "Ctrl+Shift+F5", KeyboardScope.Query),
        new("Keys.ExecuteSelection", "Ctrl+Return", KeyboardScope.Query),
        new("Keys.Format", "Ctrl+Shift+F", KeyboardScope.Query),
        new("Keys.Stop", "Escape", KeyboardScope.Query),

        new("Keys.Find", "Ctrl+F", KeyboardScope.Query),
        new("Keys.Replace", "Ctrl+H", KeyboardScope.Query),
        new("Keys.FindNext", "F3", KeyboardScope.Query),
        new("Keys.FindPrevious", "Shift+F3", KeyboardScope.Query),

        new("Keys.CopyRows", "Ctrl+C", KeyboardScope.Grid),

        // The tree's own keys (2.7). They were never listed here, which is why the map could say
        // nothing about the structure gesture while a code comment claimed it was on Ctrl+Enter.
        new("Keys.Rename", "F2", KeyboardScope.Explorer),
        new("Keys.Structure", "F4", KeyboardScope.Explorer),
        new("Keys.Drop", "Delete", KeyboardScope.Explorer),

        // Window scope, not Explorer: the panel being away is exactly when the focus is elsewhere,
        // and a key scoped to the tree could hide it and never bring it back.
        new("Keys.TogglePanel", "Ctrl+B", KeyboardScope.Window)
    ];

    #endregion

    #region Functions

    /// <summary>
    /// The gesture as it should be READ on this machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The design asks that the list show what actually works here rather than a universal table with
    /// a footnote. On macOS the modifier a person presses is ⌘, so that is what the row says.
    /// </para>
    /// <para>
    /// <c>isMac</c> is a parameter rather than a call to <see cref="OperatingSystem"/> so that both
    /// answers can be measured on one machine - the alternative is a rule about macOS that only a Mac
    /// can check, which is how the release workflow shipped two platforms of three in silence.
    /// </para>
    /// </remarks>
    public static string Display(string gesture, bool isMac) =>
        isMac ? gesture.Replace("Ctrl", "⌘") : gesture;

    public static bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    #endregion
}
