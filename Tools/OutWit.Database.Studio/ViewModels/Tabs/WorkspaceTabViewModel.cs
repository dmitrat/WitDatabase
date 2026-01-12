using System.ComponentModel;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.Utils;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.ViewModels.Tabs;

/// <summary>
/// Base class for all workspace tabs.
/// </summary>
public abstract class WorkspaceTabViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    protected WorkspaceTabViewModel(ApplicationViewModel applicationVm)
        : base(applicationVm)
    {
        InitEvents();
    }

    #endregion

    #region Initialization

    private void InitEvents()
    {
        PropertyChanged += OnBasePropertyChanged;
    }

    #endregion

    #region Abstract

    /// <summary>
    /// Gets the type of this tab.
    /// </summary>
    public abstract WorkspaceTabType TabType { get; }

    /// <summary>
    /// Gets the icon path data for this tab type.
    /// </summary>
    public abstract string IconPath { get; }

    #endregion

    #region Virtual

    /// <summary>
    /// Called when the tab is activated (selected).
    /// </summary>
    public virtual void OnActivated()
    {
    }

    /// <summary>
    /// Called when the tab is deactivated (another tab selected).
    /// </summary>
    public virtual void OnDeactivated()
    {
    }

    /// <summary>
    /// Called before the tab is closed. Return false to cancel closing.
    /// </summary>
    public virtual bool CanClose() => true;

    /// <summary>
    /// Called when the tab is being closed.
    /// </summary>
    public virtual void OnClosed()
    {
    }

    #endregion

    #region Functions

    protected void UpdateDisplayTitle()
    {
        DisplayTitle = IsModified ? $"{Title} •" : Title;
    }

    #endregion

    #region Event Handlers

    private void OnBasePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.IsProperty((WorkspaceTabViewModel vm) => vm.Title))
            UpdateDisplayTitle();

        if (e.IsProperty((WorkspaceTabViewModel vm) => vm.IsModified))
            UpdateDisplayTitle();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Tab title.
    /// </summary>
    [Notify]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Display title with modification indicator.
    /// </summary>
    [Notify]
    public string DisplayTitle { get; protected set; } = string.Empty;

    /// <summary>
    /// Indicates if the tab has unsaved changes.
    /// </summary>
    [Notify]
    public bool IsModified { get; set; }

    /// <summary>
    /// Indicates if this tab is pinned (cannot be closed, stays at the beginning).
    /// </summary>
    [Notify]
    public bool IsPinned { get; set; }

    /// <summary>
    /// Unique identifier for this tab instance.
    /// Used to prevent duplicate tabs for the same object.
    /// </summary>
    public virtual string? UniqueId => null;

    #endregion
}
