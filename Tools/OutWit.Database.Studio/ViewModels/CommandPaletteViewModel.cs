using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.Utils;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// What a palette entry is: something to run, or somewhere to go.
/// </summary>
public enum PaletteItemKind
{
    Command,
    Object
}

/// <summary>
/// One line of the palette.
/// </summary>
public sealed class PaletteItem
{
    public required PaletteItemKind Kind { get; init; }

    /// <summary>
    /// What is matched against and what is shown.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// The right-hand half of the line: the connection an object belongs to, or the shortcut of a
    /// command. With several databases open, an object's name alone does not say which one it is in.
    /// </summary>
    public string? Subtitle { get; init; }

    public required Func<Task> Invoke { get; init; }

    /// <summary>
    /// Set while filtering; higher is a better match. Kept on the item so that a test can read why
    /// the order came out the way it did.
    /// </summary>
    public int Score { get; set; }

    public override string ToString() => Subtitle == null ? Title : $"{Title} - {Subtitle}";
}

/// <summary>
/// The command palette (WS-9): one entry for both "run this" and "go to that", on Ctrl+K.
///
/// It is also the answer to the missing search in the object tree - with several connections open and
/// a folder per object type, finding a table by scrolling is the slowest thing in the application.
/// Typing three letters is not.
///
/// The list is built when the palette opens, from the tree that is already loaded, so opening it
/// costs nothing and asks the database nothing.
/// </summary>
public class CommandPaletteViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constants

    /// <summary>
    /// How many lines are shown. The palette is for finding something you can name, not for browsing.
    /// </summary>
    public const int MAX_ITEMS = 12;

    /// <summary>
    /// How many recently chosen entries are remembered for the empty prompt.
    /// </summary>
    public const int RECENT_CAPACITY = 8;

    #endregion

    #region Fields

    private readonly List<PaletteItem> m_all = [];
    private readonly List<string> m_recent = [];

    #endregion

    #region Constructors

    public CommandPaletteViewModel(ApplicationViewModel applicationVm)
        : base(applicationVm)
    {
        Items = [];

        InitCommands();
        InitEvents();
    }

    #endregion

    #region Initialization

    private void InitCommands()
    {
        OpenCommand = new RelayCommand(Open);
        CloseCommand = new RelayCommand(Close);
        AcceptCommand = new RelayCommandAsync(AcceptAsync);
        MoveDownCommand = new RelayCommand(() => Move(1));
        MoveUpCommand = new RelayCommand(() => Move(-1));
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Opens the palette and rebuilds its list from what is open right now.
    /// </summary>
    public void Open()
    {
        Rebuild();

        Query = string.Empty;
        Filter();

        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
    }

    /// <summary>
    /// Runs the selected entry and closes. Nothing happens if the list is empty - a palette that
    /// silently ran the first thing it could find would be worse than one that does nothing.
    /// </summary>
    public async Task AcceptAsync()
    {
        var item = SelectedItem;

        if (item == null)
            return;

        Close();

        Remember(item);

        await item.Invoke();
    }

    /// <summary>
    /// Rebuilds the entries: the commands Studio always has, and every object of every open
    /// connection as the tree currently knows them.
    /// </summary>
    private void Rebuild()
    {
        m_all.Clear();

        AddCommands();
        AddObjects();
    }

    private void AddCommands()
    {
        var main = ApplicationVm.MainWindowVm;
        var workspace = ApplicationVm.WorkspaceTabsVm;

        void Add(string title, string shortcut, ICommand command, object? parameter = null)
        {
            m_all.Add(new PaletteItem
            {
                Kind = PaletteItemKind.Command,
                Title = title,
                Subtitle = shortcut,
                Invoke = () =>
                {
                    if (command.CanExecute(parameter))
                        command.Execute(parameter);

                    return Task.CompletedTask;
                }
            });
        }

        Add("New query tab", "Ctrl+T", workspace.NewQueryTabCommand);
        Add("Open database...", "Ctrl+O", main.OpenDatabaseCommand);
        Add("New database...", "Ctrl+N", main.NewDatabaseCommand);
        Add("Close database", "", main.CloseDatabaseCommand);
        Add("Refresh schema", "Ctrl+R", main.RefreshCommand);
        Add("Execute query", "F5", workspace.ExecuteQueryCommand);
        Add("Export...", "", main.ExportCommand);
        Add("Import...", "", main.ImportCommand);
        Add("Settings...", "", main.SettingsCommand);
        Add("About", "", main.AboutCommand);
        Add("Exit", "", main.ExitCommand);
    }

    private void AddObjects()
    {
        foreach (var root in ApplicationVm.DatabaseExplorerVm.Nodes)
        {
            foreach (var folder in root.Children)
            {
                foreach (var node in folder.Children)
                {
                    var target = node;

                    m_all.Add(new PaletteItem
                    {
                        Kind = PaletteItemKind.Object,
                        Title = target.Name,
                        Subtitle = Localization.Format("Palette.ObjectIn", Describe(target.NodeType), root.Name),
                        Invoke = () =>
                        {
                            // Selecting the node is what "go to" means: the tree scrolls to it, and
                            // the connection it belongs to becomes the active one (WS-3).
                            ApplicationVm.DatabaseExplorerVm.SelectedNode = target;
                            return Task.CompletedTask;
                        }
                    });
                }
            }
        }
    }

    private static string Describe(DatabaseNodeType type) => type switch
    {
        DatabaseNodeType.Table => "table",
        DatabaseNodeType.View => "view",
        DatabaseNodeType.Index => "index",
        DatabaseNodeType.Trigger => "trigger",
        DatabaseNodeType.Sequence => "sequence",
        _ => type.ToString().ToLowerInvariant()
    };

    /// <summary>
    /// Filters and orders the entries.
    ///
    /// The ranking is deliberately dull: an exact name first, then what starts with the query, then
    /// what contains it. A cleverer score - fuzzy subsequences, letter distances - is the kind of
    /// thing that feels good on the day it is written and then puts the wrong table first for a year.
    /// </summary>
    public void Filter()
    {
        Items.Clear();

        var query = (Query ?? string.Empty).Trim();

        if (query.Length == 0)
        {
            foreach (var item in EmptyPrompt())
                Items.Add(item);

            SelectedItem = Items.FirstOrDefault();
            return;
        }

        var matches = new List<PaletteItem>();

        foreach (var item in m_all)
        {
            var score = ScoreOf(item.Title, query);

            if (score == 0)
                continue;

            item.Score = score;
            matches.Add(item);
        }

        foreach (var item in matches
                     .OrderByDescending(item => item.Score)
                     .ThenBy(item => item.Title.Length)
                     .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                     .Take(MAX_ITEMS))
        {
            Items.Add(item);
        }

        SelectedItem = Items.FirstOrDefault();
    }

    private static int ScoreOf(string title, string query)
    {
        if (title.Equals(query, StringComparison.OrdinalIgnoreCase))
            return 3;

        if (title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 2;

        if (title.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 1;

        return 0;
    }

    /// <summary>
    /// What the palette shows before anything is typed: what was chosen recently, then whatever else
    /// there is. An empty list would make the palette look broken on the first Ctrl+K of a session.
    /// </summary>
    private IEnumerable<PaletteItem> EmptyPrompt()
    {
        var recent = m_recent
            .Select(title => m_all.FirstOrDefault(item => item.Title == title))
            .Where(item => item != null)
            .Select(item => item!)
            .ToList();

        return recent
            .Concat(m_all.Where(item => !recent.Contains(item)))
            .Take(MAX_ITEMS);
    }

    private void Remember(PaletteItem item)
    {
        m_recent.Remove(item.Title);
        m_recent.Insert(0, item.Title);

        while (m_recent.Count > RECENT_CAPACITY)
            m_recent.RemoveAt(m_recent.Count - 1);
    }

    private void Move(int delta)
    {
        if (Items.Count == 0)
            return;

        var index = SelectedItem == null ? -1 : Items.IndexOf(SelectedItem);
        var next = index + delta;

        // Wraps: a list of five that stops moving at the fifth is a list that feels broken.
        if (next < 0)
            next = Items.Count - 1;
        else if (next >= Items.Count)
            next = 0;

        SelectedItem = Items[next];
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.IsProperty((CommandPaletteViewModel vm) => vm.Query))
            Filter();
    }

    #endregion

    #region Properties

    [Notify]
    public bool IsOpen { get; set; }

    [Notify]
    public string Query { get; set; } = string.Empty;

    public ObservableCollection<PaletteItem> Items { get; }

    [Notify]
    public PaletteItem? SelectedItem { get; set; }

    /// <summary>
    /// Everything the palette knows about, unfiltered. For tests, and for a count in the view.
    /// </summary>
    public IReadOnlyList<PaletteItem> AllItems => m_all;

    #endregion

    #region Commands

    public ICommand OpenCommand { get; private set; } = null!;

    public ICommand CloseCommand { get; private set; } = null!;

    public ICommand AcceptCommand { get; private set; } = null!;

    public ICommand MoveDownCommand { get; private set; } = null!;

    public ICommand MoveUpCommand { get; private set; } = null!;

    #endregion

    #region Localization

    private Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    #endregion
}
