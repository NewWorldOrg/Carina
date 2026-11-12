namespace Carina.Domain.Notifications;

/// <summary>
/// What one look at the ledgers found at <paramref name="At"/>: how many happenings fell within the look-back, and
/// those of them the notice ledger does not hold yet, in the order they happened.
/// </summary>
public sealed record NoticeHappenings(DateTime At, int Seen, IReadOnlyList<NoticeHappening> Fresh);

/// <summary>
/// Decides what the ledgers show that is to be taken as a notice: each happening within the look-back, up to now,
/// that no notice of the same kind taken from the same row is held for.
/// </summary>
public sealed class NotificationIntakeDomainService(
    INoticeSourceReader sources,
    INoticeRepository notices,
    TimeProvider clock)
{
    public async Task<NoticeHappenings> FreshAsync(TimeSpan looksBack, CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        DateTime since = now - looksBack;

        List<NoticeHappening> happened =
        [
            .. (await sources.SinceAsync(since, cancellationToken))
                .SelectMany(source => source.Happenings())
                .Where(happening => happening.OccurredAt >= since && happening.OccurredAt <= now)
                .OrderBy(happening => happening.OccurredAt),
        ];

        if (happened.Count is 0)
        {
            return new NoticeHappenings(now, 0, []);
        }

        IReadOnlySet<NoticeKey> held = await notices.HeldAmongAsync(
            [.. happened.Select(happening => happening.SourceId).Distinct()],
            cancellationToken);

        return new NoticeHappenings(now, happened.Count, [.. happened.Where(happening => !held.Contains(happening.Key))]);
    }
}
