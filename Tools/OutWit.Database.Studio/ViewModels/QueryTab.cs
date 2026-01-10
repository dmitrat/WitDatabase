using System.Windows.Input;
using OutWit.Common.Abstract;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.Table;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// Represents a query editor tab with its content and state.
/// </summary>
public class QueryTab : ModelBase
{
    #region Constants

    private const int DEFAULT_PAGE_SIZE = 100;

    #endregion

    #region Fields

    private TableView? m_fullResultData;
    private int m_currentPage = 1;
    private int m_pageSize = DEFAULT_PAGE_SIZE;
    private int m_totalRowCount;

    #endregion

    #region Constructors

    public QueryTab()
    {
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitCommands()
    {
        FirstPageCommand = new RelayCommand(_ => GoToFirstPage(), _ => CanGoToPreviousPage);
        PreviousPageCommand = new RelayCommand(_ => GoToPreviousPage(), _ => CanGoToPreviousPage);
        NextPageCommand = new RelayCommand(_ => GoToNextPage(), _ => CanGoToNextPage);
        LastPageCommand = new RelayCommand(_ => GoToLastPage(), _ => CanGoToNextPage);
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not QueryTab other)
            return false;

        return Id == other.Id
            && Title == other.Title
            && SqlText == other.SqlText
            && FilePath == other.FilePath;
    }

    public override QueryTab Clone()
    {
        return new QueryTab
        {
            Id = Id,
            Title = Title,
            SqlText = SqlText,
            FilePath = FilePath,
            IsModified = IsModified
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Unique identifier for the tab.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Display title of the tab.
    /// </summary>
    [Notify]
    public string Title { get; set; } = "New Query";

    /// <summary>
    /// SQL text content of the query.
    /// </summary>
    [Notify]
    public string SqlText { get; set; } = string.Empty;

    /// <summary>
    /// File path if the query is saved to a file.
    /// </summary>
    [Notify]
    public string? FilePath { get; set; }

    /// <summary>
    /// Indicates if the tab has unsaved changes.
    /// </summary>
    [Notify(NotifyAlso = nameof(DisplayTitle))]
    public bool IsModified { get; set; }

    /// <summary>
    /// Gets the display title with modification indicator.
    /// </summary>
    public string DisplayTitle => IsModified ? $"{Title} *" : Title;

    /// <summary>
    /// Header row with column names.
    /// </summary>
    [Notify]
    public TableViewRow? HeaderRow { get; set; }

    /// <summary>
    /// Current page of results for display.
    /// </summary>
    [Notify(NotifyAlso = nameof(HasResults))]
    public TableViewPage? ResultPage { get; set; }

    /// <summary>
    /// Error message from query execution.
    /// </summary>
    [Notify(NotifyAlso = nameof(IsSuccess))]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Number of rows affected by the query.
    /// </summary>
    [Notify(NotifyAlso = nameof(HasMessages))]
    public int RowsAffected { get; set; }

    /// <summary>
    /// Execution time in milliseconds.
    /// </summary>
    [Notify]
    public double ExecutionTimeMs { get; set; }

    /// <summary>
    /// Gets whether the tab has results to display.
    /// </summary>
    public bool HasResults => TotalRowCount > 0;

    /// <summary>
    /// Gets whether the query execution was successful.
    /// </summary>
    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Gets whether there are messages to display.
    /// </summary>
    public bool HasMessages => !string.IsNullOrEmpty(ErrorMessage) || RowsAffected > 0;

    #endregion

    #region Pagination Properties

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public int CurrentPage
    {
        get => m_currentPage;
        set
        {
            if (m_currentPage == value)
                return;

            m_currentPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayedRowStart));
            OnPropertyChanged(nameof(DisplayedRowEnd));
            OnPropertyChanged(nameof(CanGoToPreviousPage));
            OnPropertyChanged(nameof(CanGoToNextPage));
        }
    }

    /// <summary>
    /// Number of rows per page.
    /// </summary>
    public int PageSize
    {
        get => m_pageSize;
        set
        {
            if (m_pageSize == value)
                return;

            m_pageSize = value;
            OnPropertyChanged();
            CurrentPage = 1;
            ApplyPagination();
        }
    }

    /// <summary>
    /// Total number of rows in the result set.
    /// </summary>
    public int TotalRowCount
    {
        get => m_totalRowCount;
        set
        {
            if (m_totalRowCount == value)
                return;

            m_totalRowCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(DisplayedRowEnd));
            OnPropertyChanged(nameof(CanGoToNextPage));
            OnPropertyChanged(nameof(HasResults));
        }
    }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages => TotalRowCount > 0 ? (int)Math.Ceiling((double)TotalRowCount / PageSize) : 1;

    /// <summary>
    /// First row number displayed (1-based).
    /// </summary>
    public int DisplayedRowStart => TotalRowCount > 0 ? (CurrentPage - 1) * PageSize + 1 : 0;

    /// <summary>
    /// Last row number displayed (1-based).
    /// </summary>
    public int DisplayedRowEnd => TotalRowCount > 0 ? Math.Min(CurrentPage * PageSize, TotalRowCount) : 0;

    /// <summary>
    /// Whether navigation to previous page is available.
    /// </summary>
    public bool CanGoToPreviousPage => CurrentPage > 1;

    /// <summary>
    /// Whether navigation to next page is available.
    /// </summary>
    public bool CanGoToNextPage => CurrentPage < TotalPages;

    #endregion

    #region Commands

    public ICommand FirstPageCommand { get; private set; } = null!;

    public ICommand PreviousPageCommand { get; private set; } = null!;

    public ICommand NextPageCommand { get; private set; } = null!;

    public ICommand LastPageCommand { get; private set; } = null!;

    #endregion

    #region Command Handlers

    private void GoToFirstPage()
    {
        if (CanGoToPreviousPage)
        {
            CurrentPage = 1;
            ApplyPagination();
        }
    }

    private void GoToPreviousPage()
    {
        if (CanGoToPreviousPage)
        {
            CurrentPage--;
            ApplyPagination();
        }
    }

    private void GoToNextPage()
    {
        if (CanGoToNextPage)
        {
            CurrentPage++;
            ApplyPagination();
        }
    }

    private void GoToLastPage()
    {
        if (CanGoToNextPage)
        {
            CurrentPage = TotalPages;
            ApplyPagination();
        }
    }

    #endregion

    #region Functions

    /// <summary>
    /// Sets the full result data and applies pagination.
    /// </summary>
    public void SetResultData(TableView? data)
    {
        m_fullResultData = data;
        
        if (data == null || data.Pages.Count == 0)
        {
            TotalRowCount = 0;
            CurrentPage = 1;
            HeaderRow = null;
            ResultPage = null;
            OnPropertyChanged(nameof(DisplayedRowStart));
            OnPropertyChanged(nameof(DisplayedRowEnd));
            return;
        }

        HeaderRow = data.HeaderRow;
        TotalRowCount = data.Pages.Sum(p => p.Rows.Count);
        m_currentPage = 1;
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(DisplayedRowStart));
        OnPropertyChanged(nameof(DisplayedRowEnd));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
        
        ApplyPagination();
    }

    /// <summary>
    /// Applies pagination to show the current page of results.
    /// </summary>
    private void ApplyPagination()
    {
        if (m_fullResultData == null || m_fullResultData.Pages.Count == 0)
        {
            ResultPage = null;
            return;
        }

        // TableView ??? ????? ????????? ????? Pages
        // ???????? ??? ?????? ? ??????? ???????? ??? ???????? ?????????
        var allRows = m_fullResultData.Pages.SelectMany(p => p.Rows).ToList();
        var startIndex = (CurrentPage - 1) * PageSize;
        var endIndex = Math.Min(startIndex + PageSize, allRows.Count);

        var page = new TableViewPage(CurrentPage);
        for (var i = startIndex; i < endIndex; i++)
        {
            page.Add(allRows[i]);
        }

        ResultPage = page;
    }

    /// <summary>
    /// Clears all results and resets pagination.
    /// </summary>
    public void ClearResults()
    {
        m_fullResultData = null;
        HeaderRow = null;
        ResultPage = null;
        TotalRowCount = 0;
        CurrentPage = 1;
        RowsAffected = 0;
        ExecutionTimeMs = 0;
        ErrorMessage = null;
    }

    #endregion
}
