using System.Collections.ObjectModel;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// How loud a notification is. Only three, because a fourth would be a judgement nobody can make
/// consistently at the call site.
/// </summary>
public enum NotificationSeverity
{
    Information,
    Warning,
    Error
}

/// <summary>
/// Something that happened, which the user did not have to be interrupted for.
/// </summary>
public sealed class Notification
{
    public required NotificationSeverity Severity { get; init; }

    public required string Title { get; init; }

    public string? Detail { get; init; }

    /// <summary>
    /// The connection it happened in, when it happened in one. With several databases open, "the
    /// import finished" is only half a sentence.
    /// </summary>
    public string? Connection { get; init; }

    /// <summary>
    /// Set by the service, so that a caller cannot forget to.
    /// </summary>
    public DateTime At { get; init; } = DateTime.Now;

    public bool IsRead { get; set; }

    public override string ToString()
    {
        var where = Connection == null ? "" : $" [{Connection}]";

        return $"{At:HH:mm:ss} {Severity}{where}: {Title}";
    }
}

/// <summary>
/// The notification centre (WS-7): things that happened, kept in a list the user can go and read,
/// instead of a modal window that has to be dismissed before anything else can be done.
///
/// A modal is for a QUESTION - that is <see cref="IConfirmationService"/>. This is for an ANSWER
/// nobody asked for: an import that finished, a background refresh that failed, a schema that
/// reloaded. Studio used to put those in the status bar, where the next one overwrote the last.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Most recent first.
    /// </summary>
    ReadOnlyObservableCollection<Notification> Notifications { get; }

    /// <summary>
    /// How many have not been looked at. The dot on the bell.
    /// </summary>
    int UnreadCount { get; }

    /// <summary>
    /// Raised whenever the list or the unread count changes, so a view that is not bound to the
    /// collection itself can still follow it.
    /// </summary>
    event EventHandler? Changed;

    void Notify(Notification notification);

    void MarkAllRead();

    void Clear();
}

/// <summary>
/// Convenience over <see cref="INotificationService.Notify"/>, so a call site reads as one line.
/// </summary>
public static class NotificationServiceExtensions
{
    public static void Information(this INotificationService service, string title, string? detail = null,
        string? connection = null)
    {
        service.Notify(new Notification
        {
            Severity = NotificationSeverity.Information,
            Title = title,
            Detail = detail,
            Connection = connection
        });
    }

    public static void Warning(this INotificationService service, string title, string? detail = null,
        string? connection = null)
    {
        service.Notify(new Notification
        {
            Severity = NotificationSeverity.Warning,
            Title = title,
            Detail = detail,
            Connection = connection
        });
    }

    public static void Error(this INotificationService service, string title, string? detail = null,
        string? connection = null)
    {
        service.Notify(new Notification
        {
            Severity = NotificationSeverity.Error,
            Title = title,
            Detail = detail,
            Connection = connection
        });
    }
}
