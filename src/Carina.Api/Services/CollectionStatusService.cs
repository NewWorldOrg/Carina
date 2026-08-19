using Carina.Api.Common;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;

namespace Carina.Api.Services;

public sealed record ServiceCoverageStatus(ServiceId ServiceId, DateTime? CoveredUntil, bool MeetsWantedCoverage);

public sealed record StreamCollectionStatus(
    NetworkId NetworkId,
    TransportStreamId? TransportStreamId,
    TuningParameters Tuning,
    StreamReach Reach,
    VisitOutcome? Outcome,
    DateTime? LastAttemptedAt,
    DateTime? LastCompletedAt,
    int ConsecutiveIncomplete,
    int LastDurationMilliseconds,
    DateTime? NotBefore,
    IReadOnlyList<ServiceId> Services,
    IReadOnlyList<ServiceCoverageStatus> Coverage,
    IReadOnlyList<VisitTally> Tally);

public sealed record CollectionStatus(
    TimeSpan WantedCoverage,
    IReadOnlyList<StreamCollectionStatus> Streams,
    IReadOnlyList<RescanNotice> Rescans);

public sealed class CollectionStatusService(
    IBroadcastStreamDirectory directory,
    IStreamVisitRepository visits,
    IProgrammeRepository programmes,
    RescanNoticeBoard rescans,
    CollectionSettings settings,
    TimeProvider clock)
{
    public async Task<ServiceResult<CollectionStatus>> ReadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<IntendedStream> intended = await directory.ListIntendedAsync(cancellationToken);
        IReadOnlyList<StreamVisit> known = await visits.ListAsync(cancellationToken);
        DateTime now = clock.GetUtcNow().UtcDateTime;
        var statuses = new List<StreamCollectionStatus>(intended.Count);

        foreach (IntendedStream stream in intended)
        {
            StreamVisit? visit = stream.TransportStreamId is { } carried
                ? known.FirstOrDefault(candidate =>
                    candidate.NetworkId.Equals(stream.NetworkId)
                    && candidate.TransportStreamId.Equals(carried))
                : null;

            statuses.Add(new StreamCollectionStatus(
                stream.NetworkId,
                stream.TransportStreamId,
                stream.Tuning,
                stream.Reach,
                visit?.Outcome,
                visit?.LastAttemptedAt,
                visit?.LastCompletedAt,
                visit?.ConsecutiveIncomplete ?? 0,
                visit?.LastDurationMilliseconds ?? 0,
                visit is null ? null : CollectionBackOff.NotBefore(visit, settings),
                stream.Services,
                await CoverageAsync(stream, now, cancellationToken),
                visit?.Tally ?? []));
        }

        return ServiceResult<CollectionStatus>.Success(
            new CollectionStatus(settings.WantedCoverage, statuses, rescans.Standing));
    }

    private async Task<IReadOnlyList<ServiceCoverageStatus>> CoverageAsync(
        IntendedStream stream,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var covered = new List<ServiceCoverageStatus>(stream.Services.Count);

        foreach (ServiceId service in stream.Services)
        {
            DateTime? until = await programmes.CoveredUntilAsync(
                stream.NetworkId.Value,
                service.Value,
                cancellationToken);

            covered.Add(new ServiceCoverageStatus(
                service,
                until,
                until is { } reach && reach - now >= settings.WantedCoverage));
        }

        return covered;
    }
}
