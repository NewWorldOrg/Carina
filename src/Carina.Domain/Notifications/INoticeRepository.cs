namespace Carina.Domain.Notifications;

public interface INoticeRepository
{
    Task<Notice?> FindAsync(NoticeId id, CancellationToken cancellationToken);

    /// <summary>
    /// The notices already held among those taken from <paramref name="sourceIds"/>.
    /// </summary>
    Task<IReadOnlySet<NoticeKey>> HeldAmongAsync(IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a notice and the deliveries of it together, all or none. Answers <see langword="false"/>, having
    /// written nothing, when the ledger already holds a notice of the same kind taken from the same row.
    /// </summary>
    Task<bool> AddAsync(Notice notice, IReadOnlyList<NotificationDelivery> deliveries, CancellationToken cancellationToken);
}
