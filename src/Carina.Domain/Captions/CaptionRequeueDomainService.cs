using Carina.Domain.Recordings;

namespace Carina.Domain.Captions;

/// <summary>
/// How many recordings were put back to have their captions taken again, and the ones still ready with a record kept.
/// </summary>
public sealed record CaptionRequeue(int Requeued, IReadOnlyList<RecordingId> StillReady);

/// <summary>
/// Puts back in the queue the recordings whose row says their captions are ready while no record of them is kept, so
/// the next pass takes them again.
/// </summary>
public sealed class CaptionRequeueDomainService(ICaptionWorklist worklist, ICaptionRecords records)
{
    public async Task<CaptionRequeue> RequeueLostAsync(CancellationToken cancellationToken)
    {
        int requeued = 0;
        List<RecordingId> stillReady = [];

        foreach (RecordingId id in await worklist.ReadyAsync(cancellationToken))
        {
            if (records.Holds(id))
            {
                stillReady.Add(id);
            }
            else if (await worklist.CaptionAsync(id, CaptionState.Pending, null, cancellationToken))
            {
                requeued++;
            }
        }

        return new CaptionRequeue(requeued, stillReady);
    }
}
