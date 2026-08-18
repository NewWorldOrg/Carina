using System.Collections.Concurrent;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Events;

namespace Carina.Infrastructure.Collection;

public sealed record RescanNotice(RescanHint Hint, DateTime NoticedAt);

public sealed class RescanNoticeBoard(IAppEventPublisher events, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, RescanNotice> notices = [];

    public IReadOnlyList<RescanNotice> Standing =>
    [
        .. notices.Values
            .OrderBy(notice => notice.Hint.NetworkId.Value)
            .ThenBy(notice => notice.Hint.TransportStreamId.Value)
            .ThenBy(notice => notice.Hint.Reason),
    ];

    public void Post(IReadOnlyList<RescanHint> hints)
    {
        ArgumentNullException.ThrowIfNull(hints);

        bool posted = false;

        foreach (RescanHint hint in hints)
        {
            var notice = new RescanNotice(hint, clock.GetUtcNow().UtcDateTime);

            if (notices.TryGetValue(Key(hint), out RescanNotice? standing)
                && SaysTheSame(standing.Hint, hint))
            {
                continue;
            }

            notices[Key(hint)] = notice;
            posted = true;
        }

        if (posted)
        {
            events.Signal(AppEventName.Tuners);
        }
    }

    private static bool SaysTheSame(RescanHint standing, RescanHint arriving)
        => standing.Services.Select(service => service.Value)
            .SequenceEqual(arriving.Services.Select(service => service.Value));

    private static string Key(RescanHint hint)
        => $"{hint.NetworkId.Value}-{hint.TransportStreamId.Value}-{hint.Reason}";
}
