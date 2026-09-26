using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

/// <summary>
/// The recordings that ended the way an encode is made from, were asked to be encoded when they
/// were recorded, and that the ledger holds no job for, whatever became of one: the oldest start
/// first, and at most as many as asked for. A recording leaves this list the moment a job is
/// queued for it, so a recording that ends after the ones beside it is found wherever its start
/// puts it.
/// </summary>
public interface IEncodeIntakeReader
{
    Task<IReadOnlyList<RecordingId>> NeverQueuedAsync(int most, CancellationToken cancellationToken);
}
