using Carina.Api.Common;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;

namespace Carina.Api.Services;

public sealed record StreamCollectionStatus(
    NetworkId NetworkId,
    TransportStreamId TransportStreamId,
    VisitOutcome? Outcome,
    DateTime? LastAttemptedAt,
    DateTime? LastCompletedAt,
    int ConsecutiveIncomplete,
    int LastDurationMilliseconds,
    DateTime? NotBefore,
    IReadOnlyList<ServiceId> Services);

public sealed record CollectionStatus(
    IReadOnlyList<StreamCollectionStatus> Streams,
    IReadOnlyList<RescanNotice> Rescans);

public sealed class CollectionStatusService(
    IBroadcastStreamDirectory directory,
    IStreamVisitRepository visits,
    RescanNoticeBoard rescans,
    CollectionSettings settings)
{
    public async Task<ServiceResult<CollectionStatus>> ReadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<BroadcastStream> streams = await directory.ListAsync(cancellationToken);
        IReadOnlyList<StreamVisit> known = await visits.ListAsync(cancellationToken);
        var statuses = new List<StreamCollectionStatus>(streams.Count);

        foreach (BroadcastStream stream in streams)
        {
            StreamVisit? visit = known.FirstOrDefault(candidate =>
                candidate.NetworkId.Equals(stream.NetworkId)
                && candidate.TransportStreamId.Equals(stream.TransportStreamId));

            statuses.Add(new StreamCollectionStatus(
                stream.NetworkId,
                stream.TransportStreamId,
                visit?.Outcome,
                visit?.LastAttemptedAt,
                visit?.LastCompletedAt,
                visit?.ConsecutiveIncomplete ?? 0,
                visit?.LastDurationMilliseconds ?? 0,
                visit is null ? null : CollectionBackOff.NotBefore(visit, settings),
                stream.Services));
        }

        return ServiceResult<CollectionStatus>.Success(new CollectionStatus(statuses, rescans.Standing));
    }
}
