using Carina.Domain.Recordings;

namespace Carina.Domain.Captions;

public sealed record CaptionRequeue(int Requeued, IReadOnlyList<RecordingId> StillReady);

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
