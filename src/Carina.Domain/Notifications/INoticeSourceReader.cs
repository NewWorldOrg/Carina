namespace Carina.Domain.Notifications;

public interface INoticeSourceReader
{
    Task<IReadOnlyList<NoticeSource>> SinceAsync(DateTime since, CancellationToken cancellationToken);
}
