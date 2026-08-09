using OutWit.Common.Abstract;
using OutWit.Common.Aspects;
using OutWit.Common.Values;
using OutWit.Common.Collections;

namespace OutWit.Database.Studio.Models;

/// <summary>
/// Everything Studio remembers between sessions, in the five sections the settings window shows
/// (WS-52). One object, held live by <see cref="Services.ISettingsService"/> and persisted whenever any
/// property changes - there is no Save button, so there is nothing to press and nothing to lose.
///
/// <para>
/// Every property raises <c>PropertyChanged</c>. That is not decoration: "applied immediately" is
/// implemented by the thing that reads a setting hearing about the change, and a plain property makes
/// that impossible without a Save button to hang the notification on.
/// </para>
/// <para>
/// A property missing from an older <c>settings.json</c> takes its default here, so a file written by
/// 2.0 keeps working.
/// </para>
/// </summary>
public sealed class Settings : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not Settings other)
            return false;

        return Language.Is(other.Language)
            && Theme.Is(other.Theme)
            && RestoreConnections.Is(other.RestoreConnections)
            && RestoreTabs.Is(other.RestoreTabs)
            && CheckForUpdates.Is(other.CheckForUpdates)
            && SkippedUpdate.Is(other.SkippedUpdate)
            && RecentFiles.Is(other.RecentFiles)
            && MaxRecentFiles.Is(other.MaxRecentFiles)
            && EditorFontFamily.Is(other.EditorFontFamily)
            && EditorFontSize.Is(other.EditorFontSize)
            && ShowLineNumbers.Is(other.ShowLineNumbers)
            && ShowWhitespace.Is(other.ShowWhitespace)
            && CompleteAsYouType.Is(other.CompleteAsYouType)
            && CheckSyntaxAsYouType.Is(other.CheckSyntaxAsYouType)
            && KeywordCase.Is(other.KeywordCase)
            && DefaultRowLimit.Is(other.DefaultRowLimit)
            && RestoreUnsavedTabs.Is(other.RestoreUnsavedTabs)
            && DateTimeFormat.Is(other.DateTimeFormat)
            && NumberFormat.Is(other.NumberFormat)
            && BinaryDisplay.Is(other.BinaryDisplay)
            && GridPageSize.Is(other.GridPageSize)
            && CountRowsAutomatically.Is(other.CountRowsAutomatically)
            && AskBeforeClosingEditedTab.Is(other.AskBeforeClosingEditedTab)
            && AskBeforeDroppingObject.Is(other.AskBeforeDroppingObject)
            && AskBeforeUnfilteredWrite.Is(other.AskBeforeUnfilteredWrite)
            && AskBeforeLongScript.Is(other.AskBeforeLongScript)
            && LogLevel.Is(other.LogLevel)
            && WindowWidth.Is(other.WindowWidth, tolerance)
            && WindowHeight.Is(other.WindowHeight, tolerance)
            && WindowState.Is(other.WindowState);
    }

    public override Settings Clone()
    {
        return new Settings
        {
            Language = Language,
            Theme = Theme,
            RestoreConnections = RestoreConnections,
            RestoreTabs = RestoreTabs,
            CheckForUpdates = CheckForUpdates,
            SkippedUpdate = SkippedUpdate,
            RecentFiles = RecentFiles.ToList(),
            MaxRecentFiles = MaxRecentFiles,
            EditorFontFamily = EditorFontFamily,
            EditorFontSize = EditorFontSize,
            ShowLineNumbers = ShowLineNumbers,
            ShowWhitespace = ShowWhitespace,
            CompleteAsYouType = CompleteAsYouType,
            CheckSyntaxAsYouType = CheckSyntaxAsYouType,
            KeywordCase = KeywordCase,
            DefaultRowLimit = DefaultRowLimit,
            RestoreUnsavedTabs = RestoreUnsavedTabs,
            DateTimeFormat = DateTimeFormat,
            NumberFormat = NumberFormat,
            BinaryDisplay = BinaryDisplay,
            GridPageSize = GridPageSize,
            CountRowsAutomatically = CountRowsAutomatically,
            AskBeforeClosingEditedTab = AskBeforeClosingEditedTab,
            AskBeforeDroppingObject = AskBeforeDroppingObject,
            AskBeforeUnfilteredWrite = AskBeforeUnfilteredWrite,
            AskBeforeLongScript = AskBeforeLongScript,
            LogLevel = LogLevel,
            WindowWidth = WindowWidth,
            WindowHeight = WindowHeight,
            WindowState = WindowState
        };
    }

    #endregion

    #region Functions

    /// <summary>
    /// Takes every value from <paramref name="other"/>, property by property, so that anything bound to
    /// this object hears about it.
    ///
    /// <para>
    /// This exists instead of replacing the object. The settings are held live and the whole
    /// application binds to <b>one</b> instance; swapping it would leave every open window reading a
    /// settings object nobody writes to any more - which looks exactly like a setting that stopped
    /// applying.
    /// </para>
    /// </summary>
    public void CopyFrom(Settings other)
    {
        Language = other.Language;
        Theme = other.Theme;
        RestoreConnections = other.RestoreConnections;
        RestoreTabs = other.RestoreTabs;
        CheckForUpdates = other.CheckForUpdates;
        SkippedUpdate = other.SkippedUpdate;
        RecentFiles = other.RecentFiles.ToList();
        MaxRecentFiles = other.MaxRecentFiles;
        EditorFontFamily = other.EditorFontFamily;
        EditorFontSize = other.EditorFontSize;
        ShowLineNumbers = other.ShowLineNumbers;
        ShowWhitespace = other.ShowWhitespace;
        CompleteAsYouType = other.CompleteAsYouType;
        CheckSyntaxAsYouType = other.CheckSyntaxAsYouType;
        KeywordCase = other.KeywordCase;
        DefaultRowLimit = other.DefaultRowLimit;
        RestoreUnsavedTabs = other.RestoreUnsavedTabs;
        DateTimeFormat = other.DateTimeFormat;
        NumberFormat = other.NumberFormat;
        BinaryDisplay = other.BinaryDisplay;
        GridPageSize = other.GridPageSize;
        CountRowsAutomatically = other.CountRowsAutomatically;
        AskBeforeClosingEditedTab = other.AskBeforeClosingEditedTab;
        AskBeforeDroppingObject = other.AskBeforeDroppingObject;
        AskBeforeUnfilteredWrite = other.AskBeforeUnfilteredWrite;
        AskBeforeLongScript = other.AskBeforeLongScript;
        LogLevel = other.LogLevel;
        WindowWidth = other.WindowWidth;
        WindowHeight = other.WindowHeight;
        WindowState = other.WindowState;
    }

    #endregion

    #region General

    /// <summary>
    /// The interface language - <c>en</c> or <c>ru</c> (WS-63). It is not a culture and does not become
    /// one: how a value is written is <see cref="NumberFormat"/> and <see cref="DateTimeFormat"/>,
    /// which are deliberately separate settings (WS-65).
    /// </summary>
    [Notify]
    public string Language { get; set; } = "en";

    /// <summary>Light, Dark or System.</summary>
    [Notify]
    public string Theme { get; set; } = "Light";

    /// <summary>Reopen the connections of the last session at startup.</summary>
    [Notify]
    public bool RestoreConnections { get; set; } = true;

    /// <summary>Reopen the tabs of the last session at startup.</summary>
    [Notify]
    public bool RestoreTabs { get; set; } = true;

    /// <summary>
    /// Ask the release feed whether a newer Studio exists (WS-70). <b>Off by default</b>: a tool that
    /// reaches the network from the machine holding a production database should be told to, once and
    /// explicitly.
    /// </summary>
    [Notify]
    public bool CheckForUpdates { get; set; }

    /// <summary>
    /// The version the user pressed "skip" on, so it is not offered again (9.8). Per VERSION rather
    /// than for good: a later one is still offered, and turning the check off is what
    /// <see cref="CheckForUpdates"/> is for.
    /// </summary>
    public string? SkippedUpdate { get; set; }

    /// <summary>Recently opened databases, most recent first.</summary>
    [Notify]
    public List<string> RecentFiles { get; set; } = [];

    [Notify]
    public int MaxRecentFiles { get; set; } = 10;

    #endregion

    #region Editor

    [Notify]
    public string EditorFontFamily { get; set; } = "Cascadia Code";

    [Notify]
    public int EditorFontSize { get; set; } = 14;

    [Notify]
    public bool ShowLineNumbers { get; set; } = true;

    [Notify]
    public bool ShowWhitespace { get; set; }

    [Notify]
    public bool CompleteAsYouType { get; set; } = true;

    [Notify]
    public bool CheckSyntaxAsYouType { get; set; } = true;

    /// <summary>How the formatter writes keywords: <c>Upper</c>, <c>Lower</c> or <c>AsTyped</c>.</summary>
    [Notify]
    public string KeywordCase { get; set; } = "Upper";

    /// <summary>The row limit a new query tab starts with.</summary>
    [Notify]
    public int DefaultRowLimit { get; set; } = 1000;

    /// <summary>Keep the text of unsaved query tabs and bring them back at startup.</summary>
    [Notify]
    public bool RestoreUnsavedTabs { get; set; } = true;

    #endregion

    #region Data

    /// <summary>
    /// <c>Iso</c> or <c>System</c> (WS-65). ISO is the default whatever the machine's locale is,
    /// because a date read out of the grid is pasted into SQL, and <c>28.06.2026</c> is neither
    /// sortable as text nor acceptable to the parser.
    /// </summary>
    [Notify]
    public string DateTimeFormat { get; set; } = "Iso";

    /// <summary>
    /// <c>Invariant</c> or <c>System</c> (WS-65). Invariant means a dot and no group separator:
    /// <c>4812.50</c> pastes into a statement, <c>4 812,50</c> does not.
    /// </summary>
    [Notify]
    public string NumberFormat { get; set; } = "Invariant";

    /// <summary>How a BLOB is shown in a cell: <c>Size</c>, <c>Hex</c> or <c>Base64</c>.</summary>
    [Notify]
    public string BinaryDisplay { get; set; } = "Size";

    [Notify]
    public int GridPageSize { get; set; } = 1000;

    [Notify]
    public bool CountRowsAutomatically { get; set; } = true;

    #endregion

    #region The catalogue of confirmations (WS-67)

    /// <summary>
    /// Every modal question Studio asks is listed here and can be turned off. The list IS the complete
    /// set of questions: one that is not here is one nobody can stop, and an interface where "Yes"
    /// cannot be stopped is one where nobody reads the single question that mattered.
    /// </summary>
    [Notify]
    public bool AskBeforeClosingEditedTab { get; set; } = true;

    [Notify]
    public bool AskBeforeDroppingObject { get; set; } = true;

    /// <summary>Ask before running an <c>UPDATE</c> or <c>DELETE</c> with no <c>WHERE</c>.</summary>
    [Notify]
    public bool AskBeforeUnfilteredWrite { get; set; } = true;

    /// <summary>Ask before running a script of more than twenty statements. Off by default.</summary>
    [Notify]
    public bool AskBeforeLongScript { get; set; }

    #endregion

    #region Diagnostics

    /// <summary><c>Errors</c>, <c>Normal</c> or <c>Verbose</c>. Verbose writes query text.</summary>
    [Notify]
    public string LogLevel { get; set; } = "Normal";

    #endregion

    #region Window

    [Notify]
    public double WindowWidth { get; set; } = 1200;

    [Notify]
    public double WindowHeight { get; set; } = 800;

    [Notify]
    public string WindowState { get; set; } = "Normal";

    #endregion
}
