using System.ComponentModel;
using System.Windows.Input;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// What the band needs from the thing it is searching. Implemented by the query tab; a test drives it
/// directly, which is what keeps the band's whole behaviour measurable without a window.
/// </summary>
public interface ISearchTarget
{
    string Text { get; set; }

    int CaretOffset { get; }

    int SelectionStart { get; }

    int SelectionLength { get; }

    /// <summary>Puts the editor's selection on a match, which is how "current" is shown.</summary>
    void Select(int offset, int length);
}

/// <summary>
/// Find and replace inside the editor (9.7): a band in the tab, not a window.
/// </summary>
/// <remarks>
/// <para>
/// <b>A band rather than a dialog, and that is a decision about the text.</b> A modal find window
/// covers the thing being searched, so the design puts a strip above the editor and leaves the text
/// visible - which also means the band has to say where it is ("2 of 5") rather than relying on the
/// user seeing the highlight.
/// </para>
/// <para>
/// <b>Nothing here decides what a match IS.</b> That is <see cref="SqlSearch"/>, a function of the
/// text, tested on its own. This object is about WHEN the question is asked - on every keystroke in
/// the box, on every toggle, and after a replacement - and about what the band then shows.
/// </para>
/// </remarks>
public class EditorSearchViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Fields

    private readonly ISearchTarget m_target;

    /// <summary>
    /// Set while a replacement is being written back, so the target's own change notification does
    /// not re-enter and re-run the search on half-applied text.
    /// </summary>
    private bool m_writing;

    #endregion

    #region Constructors

    public EditorSearchViewModel(ApplicationViewModel applicationVm, ISearchTarget target)
        : base(applicationVm)
    {
        m_target = target;

        InitCommands();
        InitEvents();

        Refresh();
    }

    #endregion

    #region Initialization

    private void InitCommands()
    {
        FindNextCommand = new RelayCommand(() => Step(+1), () => HasMatches);
        FindPreviousCommand = new RelayCommand(() => Step(-1), () => HasMatches);
        ReplaceCommand = new RelayCommand(Replace, () => CanReplace);
        ReplaceAllCommand = new RelayCommand(ReplaceAll, () => CanReplace);
        CloseCommand = new RelayCommand(Close);
        ToggleReplaceCommand = new RelayCommand(() => IsReplaceMode = !IsReplaceMode);
    }

    /// <summary>
    /// One subscription at the top, and it is not tidiness.
    /// </summary>
    /// <remarks>
    /// <c>RelayCommand</c> does not re-ask <c>CanExecute</c> unless it is told, and a COMPUTED
    /// property like <see cref="HasMatches"/> raises nothing of its own - so without this the Next
    /// button stays grey in front of a box with five matches in it. That defect has been found by
    /// running the application three times in this project; here it is wired before it can happen.
    /// </remarks>
    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Term):
            case nameof(MatchCase):
            case nameof(UseRegex):
            case nameof(WholeWord):
            case nameof(InSelection):
                Refresh();
                break;
        }

        RaiseCanExecute();
    }

    #endregion

    #region Functions

    /// <summary>
    /// Opens the band, taking the term from what is selected in the editor when there is one - which
    /// is what every editor does and what makes Ctrl+F on a word a single keystroke.
    /// </summary>
    public void Open(bool replace)
    {
        if (m_target.SelectionLength > 0 && !InSelection)
        {
            var selected = Selected();

            // A multi-line selection means "search in here", not "search for all of this". And a
            // selection of nothing but whitespace is not a term either: found in the running
            // application, where Ctrl+H picked up a stray one-character selection and opened the band
            // announcing "1 of 15" - the number of SPACES in the query - over an empty-looking box.
            // Every editor ignores it, and the reason is exactly that: the count is unreadable noise
            // about something the person did not ask for.
            if (!selected.Contains('\n') && !string.IsNullOrWhiteSpace(selected))
                Term = selected;
        }

        IsReplaceMode = replace;
        IsOpen = true;

        Refresh();
    }

    public void Close()
    {
        IsOpen = false;
    }

    /// <summary>
    /// Asks the text again and re-states where we are in it.
    /// </summary>
    /// <remarks>
    /// Public because the tab calls it when the TEXT changes underneath the band: a count that was
    /// true a moment ago is worse than no count, and the band stays open while the query is edited.
    /// </remarks>
    public void Refresh()
    {
        var outcome = SqlSearch.Find(m_target.Text, Term, Options());

        Matches = outcome.Matches;
        PatternError = outcome.PatternError;

        if (Matches.Count == 0)
        {
            Current = -1;
        }
        else
        {
            // Keep the place if the same match is still under us; otherwise the one at the caret.
            Current = Current >= 0 && Current < Matches.Count
                ? Current
                : SqlSearch.IndexAtOrAfter(Matches, m_target.CaretOffset);
        }

        Describe();
    }

    private void Step(int direction)
    {
        if (Matches.Count == 0)
            return;

        Current = Current < 0
            ? SqlSearch.IndexAtOrAfter(Matches, m_target.CaretOffset)
            : (Current + direction + Matches.Count) % Matches.Count;

        Show();
        Describe();
    }

    private void Replace()
    {
        if (Current < 0 || Current >= Matches.Count)
            return;

        var match = Matches[Current];
        var at = Current;

        Write(SqlSearch.ReplaceOne(m_target.Text, match, Replacement, Options(), Term));

        Refresh();

        // Stay where the replacement happened rather than jumping to the first match: pressing
        // Replace repeatedly has to walk forwards through the text.
        if (Matches.Count > 0)
        {
            Current = Math.Min(at, Matches.Count - 1);
            Show();
            Describe();
        }
    }

    private void ReplaceAll()
    {
        var (text, count) = SqlSearch.ReplaceAll(m_target.Text, Term, Replacement, Options());

        if (count == 0)
            return;

        Write(text);

        Replaced = count;
        Refresh();
    }

    /// <summary>
    /// Writes the text back through the target, with the re-entrancy flag up.
    /// </summary>
    private void Write(string text)
    {
        m_writing = true;

        try
        {
            m_target.Text = text;
        }
        finally
        {
            m_writing = false;
        }
    }

    private void Show()
    {
        if (Current < 0 || Current >= Matches.Count)
            return;

        var match = Matches[Current];

        m_target.Select(match.Offset, match.Length);
    }

    /// <summary>
    /// What the band says about where we are. Three states, and the third is the one an editor usually
    /// gets wrong: a pattern that is not finished is not the same as a text with nothing in it.
    /// </summary>
    private void Describe()
    {
        if (PatternError != null)
        {
            Summary = Localization["Search.BadPattern"];
            return;
        }

        if (string.IsNullOrEmpty(Term))
        {
            Summary = string.Empty;
            return;
        }

        Summary = Matches.Count == 0
            ? Localization["Search.NoMatches"]
            : Localization.Format("Search.Position", Current + 1, Matches.Count);
    }

    private SearchOptions Options()
    {
        var start = InSelection ? m_target.SelectionStart : 0;
        var length = InSelection ? m_target.SelectionLength : 0;

        return new SearchOptions(MatchCase, UseRegex, WholeWord, start, length);
    }

    private string Selected()
    {
        var text = m_target.Text;
        var start = Math.Clamp(m_target.SelectionStart, 0, text.Length);
        var length = Math.Clamp(m_target.SelectionLength, 0, text.Length - start);

        return text.Substring(start, length);
    }

    private void RaiseCanExecute()
    {
        (FindNextCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (FindPreviousCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ReplaceCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ReplaceAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    /// <summary>Whether the band should re-read the text after the target changed it.</summary>
    public bool IsWriting => m_writing;

    #endregion

    #region Properties

    [Notify]
    public bool IsOpen { get; set; }

    [Notify]
    public bool IsReplaceMode { get; set; }

    [Notify]
    public string? Term { get; set; }

    [Notify]
    public string? Replacement { get; set; }

    [Notify]
    public bool MatchCase { get; set; }

    [Notify]
    public bool UseRegex { get; set; }

    [Notify]
    public bool WholeWord { get; set; }

    [Notify]
    public bool InSelection { get; set; }

    /// <summary>Every match in the text as it stands.</summary>
    [Notify]
    public IReadOnlyList<SearchMatch> Matches { get; private set; } = [];

    /// <summary>Which match is current, zero-based, or -1 when there is none.</summary>
    [Notify]
    public int Current { get; private set; } = -1;

    /// <summary>Why the pattern could not be used, or null.</summary>
    [Notify]
    public string? PatternError { get; private set; }

    /// <summary>How many the last Replace All wrote, for the notification the tab shows.</summary>
    [Notify]
    public int Replaced { get; private set; }

    /// <summary>"2 of 5", "no matches", or what is wrong with the pattern.</summary>
    [Notify]
    public string Summary { get; private set; } = string.Empty;

    public bool HasMatches => Matches.Count > 0;

    public bool CanReplace => HasMatches && IsReplaceMode;

    #endregion

    #region Commands

    public ICommand FindNextCommand { get; private set; } = null!;

    public ICommand FindPreviousCommand { get; private set; } = null!;

    public ICommand ReplaceCommand { get; private set; } = null!;

    public ICommand ReplaceAllCommand { get; private set; } = null!;

    public ICommand CloseCommand { get; private set; } = null!;

    public ICommand ToggleReplaceCommand { get; private set; } = null!;

    #endregion

    #region Tools

    private Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    #endregion
}
