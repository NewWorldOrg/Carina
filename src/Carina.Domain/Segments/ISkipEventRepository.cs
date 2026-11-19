using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public interface ISkipEventRepository
{
    Task<bool> TakeAsync(SkipEvent skip, CancellationToken cancellationToken);

    Task<IReadOnlyList<SkipEvent>> ListAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken);

    Task<SkipReturns> ReturnsAsync(int modelVersion, DateTime from, DateTime until, CancellationToken cancellationToken);
}
