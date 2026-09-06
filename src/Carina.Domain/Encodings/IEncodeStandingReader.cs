using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

public interface IEncodeStandingReader
{
    Task<EncodeStandingBoard> ReadAsync(
        IReadOnlyCollection<RecordingId> recordings,
        CancellationToken cancellationToken);
}
