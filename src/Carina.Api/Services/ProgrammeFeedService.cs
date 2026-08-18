using Carina.Api.Common;
using Carina.Domain.Programmes;

namespace Carina.Api.Services;

public sealed record FeedPage(
    IReadOnlyList<Programme> Programmes,
    BulkCursor Next,
    bool StartOver);

public sealed class ProgrammeFeedService(
    IProgrammeRepository programmes,
    ICollectionEpochRepository epochs,
    TimeProvider clock)
{
    public async Task<ServiceResult<FeedPage>> ReadAsync(
        BulkCursor? asked,
        int rows,
        CancellationToken cancellationToken)
    {
        CollectionEpoch epoch = await epochs.ReadAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);
        BulkCursor from = asked ?? BulkCursor.Beginning(epoch.Generation);

        if (from.Generation != epoch.Generation)
        {
            return ServiceResult<FeedPage>.Success(new FeedPage(
                [],
                BulkCursor.Beginning(epoch.Generation),
                StartOver: true));
        }

        IReadOnlyList<Programme> carried = await programmes.ListAfterAsync(
            from.Revision,
            rows,
            cancellationToken);
        long reached = carried.Count == 0 ? from.Revision : carried[^1].Revision;

        return ServiceResult<FeedPage>.Success(new FeedPage(
            carried,
            new BulkCursor(epoch.Generation, reached),
            StartOver: false));
    }
}
