using Carina.Api.Services;
using Carina.Contracts;
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

public sealed record StreamTuningResponder(TuneSystem System, int PhysicalChannel, int? TransportStreamId)
{
    public static StreamTuningResponder Of(TuningParameters tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        return new StreamTuningResponder(
            tuning.System,
            tuning.PhysicalChannel,
            tuning.TransportStreamId?.Value);
    }
}

public sealed record StreamRotationResponder(
    RotationState State,
    int ConsecutiveFailures,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? NeedsAttentionSince)
{
    public static StreamRotationResponder Of(StreamReach reach)
    {
        ArgumentNullException.ThrowIfNull(reach);

        return new StreamRotationResponder(
            reach.State,
            reach.ConsecutiveFailures,
            Moments.Of(reach.NextAttemptAt),
            Moments.Of(reach.NeedsAttentionSince));
    }
}

public sealed record ServiceCoverageResponder(
    int ServiceId,
    DateTimeOffset? CoveredUntil,
    bool MeetsWantedCoverage)
{
    public static ServiceCoverageResponder Of(ServiceCoverageStatus coverage)
    {
        ArgumentNullException.ThrowIfNull(coverage);

        return new ServiceCoverageResponder(
            coverage.ServiceId.Value,
            Moments.Of(coverage.CoveredUntil),
            coverage.MeetsWantedCoverage);
    }
}

public sealed record VisitTallyResponder(
    int ServiceId,
    int TableId,
    int LastTableId,
    int SegmentsDeclared,
    int SegmentsHeard,
    int SectionsDeclared,
    int SectionsHeard,
    int VersionChanges)
{
    public static VisitTallyResponder Of(VisitTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);

        return new VisitTallyResponder(
            tally.ServiceId.Value,
            tally.TableId,
            tally.LastTableId,
            tally.SegmentsDeclared,
            tally.SegmentsHeard,
            tally.SectionsDeclared,
            tally.SectionsHeard,
            tally.VersionChanges);
    }
}

public sealed record StreamCollectionStatusResponder(
    int NetworkId,
    int? TransportStreamId,
    StreamTuningResponder Tuning,
    StreamRotationResponder Rotation,
    StreamCollectionOutcome Outcome,
    DateTimeOffset? LastAttemptedAt,
    DateTimeOffset? LastCompletedAt,
    int ConsecutiveIncomplete,
    int LastDurationMilliseconds,
    DateTimeOffset? NotBefore,
    IReadOnlyList<int> ServiceIds,
    IReadOnlyList<ServiceCoverageResponder> Coverage,
    IReadOnlyList<VisitTallyResponder> Tally)
{
    public static StreamCollectionStatusResponder Of(StreamCollectionStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new StreamCollectionStatusResponder(
            status.NetworkId.Value,
            status.TransportStreamId?.Value,
            StreamTuningResponder.Of(status.Tuning),
            StreamRotationResponder.Of(status.Reach),
            Reached(status.Outcome),
            Moments.Of(status.LastAttemptedAt),
            Moments.Of(status.LastCompletedAt),
            status.ConsecutiveIncomplete,
            status.LastDurationMilliseconds,
            Moments.Of(status.NotBefore),
            [.. status.Services.Select(service => service.Value)],
            [.. status.Coverage.Select(ServiceCoverageResponder.Of)],
            [.. status.Tally.Select(VisitTallyResponder.Of)]);
    }

    private static StreamCollectionOutcome Reached(VisitOutcome? outcome)
        => outcome is null
            ? StreamCollectionOutcome.NeverVisited
            : (StreamCollectionOutcome)(int)outcome.Value;
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
    int WantedCoverageHours,
    IReadOnlyList<StreamCollectionStatusResponder> Streams,
    IReadOnlyList<RescanNoticeResponder> Rescans)
{
    public static CollectionStatusResponder Of(CollectionStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new CollectionStatusResponder(
            (int)status.WantedCoverage.TotalHours,
            [.. status.Streams.Select(StreamCollectionStatusResponder.Of)],
            [.. status.Rescans.Select(RescanNoticeResponder.Of)]);
    }
}

file static class Moments
{
    public static DateTimeOffset? Of(DateTime? at)
        => at is null ? null : new DateTimeOffset(at.Value, TimeSpan.Zero);
}
