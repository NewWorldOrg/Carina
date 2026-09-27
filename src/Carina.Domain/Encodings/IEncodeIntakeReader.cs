using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

/// <summary>
/// Reads the recordings to queue for an encode: those that ended the way an encode is made from,
/// were asked to be encoded when they were recorded, have no files left behind by a deletion, and
/// have no job in the ledger. The oldest start comes first, at most as many as asked for.
/// </summary>
public interface IEncodeIntakeReader
{
    Task<IReadOnlyList<RecordingId>> NeverQueuedAsync(int most, CancellationToken cancellationToken);
}
