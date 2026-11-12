using Carina.Domain.Recordings;

namespace Carina.Domain.DataBroadcast;

/// <summary>
/// Puts back in the queue the recordings whose row says the record of their data broadcast is made while no record of
/// it is kept, so the next pass takes it again.
/// </summary>
public sealed class DataBroadcastRequeueDomainService(IDataBroadcastWorklist worklist, IDataBroadcastRecords records)
{
    /// <summary>
    /// How many recordings were put back; one that went from the ledger before it was put back is not counted.
    /// </summary>
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
