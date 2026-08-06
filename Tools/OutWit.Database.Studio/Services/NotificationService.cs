using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Implementation of <see cref="INotificationService"/>: a bounded, newest-first list.
///
/// Bounded on purpose. A session that imports a hundred files should not end up holding a hundred
/// entries nobody will scroll to; the oldest fall off, and the log file is where the whole history
/// lives - every notification is written there as well, which is what makes this safe to trim.
/// </summary>
public sealed class NotificationService : INotificationService
{
    #region Constants

    /// <summary>
    /// How many are kept. Enough to cover what happened while the user was looking elsewhere.
    /// </summary>
    public const int CAPACITY = 50;

    #endregion

    #region Fields

    private readonly ObservableCollection<Notification> m_notifications = [];
    private readonly ILogger<NotificationService> m_logger;

    #endregion

    #region Events

    public event EventHandler? Changed;

    #endregion

    #region Constructors

    public NotificationService(ILogger<NotificationService> logger)
    {
        m_logger = logger;

        Notifications = new ReadOnlyObservableCollection<Notification>(m_notifications);
    }

    #endregion

    #region Functions

    public void Notify(Notification notification)
    {
        m_notifications.Insert(0, notification);

        while (m_notifications.Count > CAPACITY)
            m_notifications.RemoveAt(m_notifications.Count - 1);

        // Also to the log, always: the list is trimmed and the file is not, and a user asked to
        // attach studio.log to an issue should not have to reproduce the notification first.
        var level = notification.Severity switch
        {
            NotificationSeverity.Error => LogLevel.Error,
            NotificationSeverity.Warning => LogLevel.Warning,
            _ => LogLevel.Information
        };

        m_logger.Log(level, "{Notification}", notification.ToString());

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkAllRead()
    {
        foreach (var notification in m_notifications)
            notification.IsRead = true;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        m_notifications.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Properties

    public ReadOnlyObservableCollection<Notification> Notifications { get; }

    public int UnreadCount => m_notifications.Count(notification => !notification.IsRead);

    #endregion
}
