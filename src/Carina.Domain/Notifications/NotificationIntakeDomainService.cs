namespace Carina.Domain.Notifications;

public sealed record NoticeHappenings(DateTime At, int Seen, IReadOnlyList<NoticeHappening> Fresh);

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
