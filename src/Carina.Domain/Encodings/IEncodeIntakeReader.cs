using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

/// <summary>
/// The recordings that ended the way an encode is made from, were asked to be encoded when they
/// were recorded, whose deletion has left no files behind, and that the ledger holds no job for,
/// whatever became of one: the oldest start first, and at most as many as asked for.
/// </summary>
public interface IEncodeIntakeReader
{
    Task<IReadOnlyList<RecordingId>> NeverQueuedAsync(int most, CancellationToken cancellationToken);
}
