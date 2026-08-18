using Carina.Api.Services;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;

namespace Carina.Api.Responder.Epg;

public enum StreamCollectionOutcome
{
    NeverVisited = 0,

    Complete = 1,

    BasicOnly = 2,

    Incomplete = 3,

    Interrupted = 4,

    NoLock = 5,

    NoBytes = 6,
}

public sealed record StreamCollectionStatusResponder(
    int NetworkId,
    int TransportStreamId,
    StreamCollectionOutcome Outcome,
    DateTimeOffset? LastAttemptedAt,
    DateTimeOffset? LastCompletedAt,
    int ConsecutiveIncomplete,
    int LastDurationMilliseconds,
    DateTimeOffset? NotBefore,
    IReadOnlyList<int> ServiceIds)
{
    public static StreamCollectionStatusResponder Of(StreamCollectionStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new StreamCollectionStatusResponder(
            status.NetworkId.Value,
            status.TransportStreamId.Value,
            Reached(status.Outcome),
            Moment(status.LastAttemptedAt),
            Moment(status.LastCompletedAt),
            status.ConsecutiveIncomplete,
            status.LastDurationMilliseconds,
            Moment(status.NotBefore),
            [.. status.Services.Select(service => service.Value)]);
    }

    private static StreamCollectionOutcome Reached(VisitOutcome? outcome)
        => outcome is null
            ? StreamCollectionOutcome.NeverVisited
            : (StreamCollectionOutcome)(int)outcome.Value;

    private static DateTimeOffset? Moment(DateTime? at)
        => at is null ? null : new DateTimeOffset(at.Value, TimeSpan.Zero);
}

public sealed record RescanNoticeResponder(
    int NetworkId,
    int TransportStreamId,
    RescanReason Reason,
    IReadOnlyList<int> ServiceIds,
    DateTimeOffset NoticedAt)
{
    public static RescanNoticeResponder Of(RescanNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        return new RescanNoticeResponder(
            notice.Hint.NetworkId.Value,
            notice.Hint.TransportStreamId.Value,
            notice.Hint.Reason,
            [.. notice.Hint.Services.Select(service => service.Value)],
            new DateTimeOffset(notice.NoticedAt, TimeSpan.Zero));
    }
}

public sealed record CollectionStatusResponder(
    IReadOnlyList<StreamCollectionStatusResponder> Streams,
    IReadOnlyList<RescanNoticeResponder> Rescans)
{
    public static CollectionStatusResponder Of(CollectionStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new CollectionStatusResponder(
            [.. status.Streams.Select(StreamCollectionStatusResponder.Of)],
            [.. status.Rescans.Select(RescanNoticeResponder.Of)]);
    }
}
