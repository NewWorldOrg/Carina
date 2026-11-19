namespace Carina.Domain.Notifications;

public interface INoticeRepository
{
    Task<Notice?> FindAsync(NoticeId id, CancellationToken cancellationToken);

    Task<IReadOnlySet<NoticeKey>> HeldAmongAsync(IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken);

    Task<bool> AddAsync(Notice notice, IReadOnlyList<NotificationDelivery> deliveries, CancellationToken cancellationToken);

    Task<int> RemoveBeforeAsync(DateTime before, CancellationToken cancellationToken);
}
