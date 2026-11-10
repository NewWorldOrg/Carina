namespace Carina.Domain.Notifications;

public interface INoticeSourceReader
{
    /// <summary>
    /// Reads, from the ledgers of recordings, reservation outcomes, encodes and quality incidents, each row that
    /// shows something to have happened at or after <paramref name="since"/>.
    /// </summary>
    Task<IReadOnlyList<NoticeSource>> SinceAsync(DateTime since, CancellationToken cancellationToken);
}
