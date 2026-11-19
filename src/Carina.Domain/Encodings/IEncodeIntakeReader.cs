using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

public interface IEncodeIntakeReader
{
    Task<IReadOnlyList<RecordingId>> NeverQueuedAsync(int most, CancellationToken cancellationToken);
}
