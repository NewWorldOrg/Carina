using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public interface ISeekEventRepository
{
    Task<bool> TakeAsync(SeekEvent seek, CancellationToken cancellationToken);

    Task<IReadOnlyList<SeekEvent>> ListAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken);
}
