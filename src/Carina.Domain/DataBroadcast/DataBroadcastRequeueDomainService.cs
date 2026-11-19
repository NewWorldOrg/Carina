using Carina.Domain.Recordings;

namespace Carina.Domain.DataBroadcast;

public sealed class DataBroadcastRequeueDomainService(IDataBroadcastWorklist worklist, IDataBroadcastRecords records)
{
    public async Task<int> RequeueLostAsync(CancellationToken cancellationToken)
    {
        int requeued = 0;

        foreach (RecordingId id in await worklist.MadeAsync(cancellationToken))
        {
            if (!records.Holds(id) && await worklist.LostAsync(id, cancellationToken))
            {
                requeued++;
            }
        }

        return requeued;
    }
}
