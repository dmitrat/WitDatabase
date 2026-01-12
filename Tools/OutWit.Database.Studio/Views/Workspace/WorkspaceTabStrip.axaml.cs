using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OutWit.Database.Studio.Views.Workspace;

/// <summary>
/// Tab strip for workspace tabs with scroll arrow support.
/// </summary>
public partial class WorkspaceTabStrip : UserControl
{
    #region Constants

    private const double SCROLL_AMOUNT = 100;

    #endregion

    #region Fields

    private ScrollViewer? m_scrollViewer;
    private Button? m_scrollLeftButton;
    private Button? m_scrollRightButton;

    #endregion

    #region Constructors

    public WorkspaceTabStrip()
    {
        InitializeComponent();
    }

    #endregion

    #region Overrides

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        m_scrollViewer = this.FindControl<ScrollViewer>("TabScrollViewer");
        m_scrollLeftButton = this.FindControl<Button>("ScrollLeftButton");
        m_scrollRightButton = this.FindControl<Button>("ScrollRightButton");

        if (m_scrollViewer != null)
        {
            m_scrollViewer.ScrollChanged += OnScrollChanged;
            m_scrollViewer.PropertyChanged += OnScrollViewerPropertyChanged;
            UpdateScrollButtonsVisibility();
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (m_scrollViewer != null)
        {
            m_scrollViewer.ScrollChanged -= OnScrollChanged;
            m_scrollViewer.PropertyChanged -= OnScrollViewerPropertyChanged;
        }

        base.OnUnloaded(e);
    }

    #endregion

    #region Event Handlers

    private void OnScrollLeftClick(object? sender, RoutedEventArgs e)
    {
        if (m_scrollViewer == null)
            return;

        var newOffset = m_scrollViewer.Offset.X - SCROLL_AMOUNT;
        m_scrollViewer.Offset = new Vector(System.Math.Max(0, newOffset), m_scrollViewer.Offset.Y);
    }

    private void OnScrollRightClick(object? sender, RoutedEventArgs e)
    {
        if (m_scrollViewer == null)
            return;

        var maxOffset = m_scrollViewer.Extent.Width - m_scrollViewer.Viewport.Width;
        var newOffset = m_scrollViewer.Offset.X + SCROLL_AMOUNT;
        m_scrollViewer.Offset = new Vector(System.Math.Min(maxOffset, newOffset), m_scrollViewer.Offset.Y);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateScrollButtonsVisibility();
    }

    private void OnScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.ExtentProperty || 
            e.Property == ScrollViewer.ViewportProperty)
        {
            UpdateScrollButtonsVisibility();
        }
    }

    #endregion

    #region Tools

    private void UpdateScrollButtonsVisibility()
    {
        if (m_scrollViewer == null || m_scrollLeftButton == null || m_scrollRightButton == null)
            return;

        var canScrollLeft = m_scrollViewer.Offset.X > 0;
        var canScrollRight = m_scrollViewer.Offset.X < m_scrollViewer.Extent.Width - m_scrollViewer.Viewport.Width - 1;
        var hasOverflow = m_scrollViewer.Extent.Width > m_scrollViewer.Viewport.Width;

        m_scrollLeftButton.IsVisible = hasOverflow && canScrollLeft;
        m_scrollRightButton.IsVisible = hasOverflow && canScrollRight;
    }

    #endregion
}
