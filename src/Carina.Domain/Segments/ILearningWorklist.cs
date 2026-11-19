using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public interface ILearningWorklist
{
    Task<IReadOnlyList<Recording>> BeingRecordedAsync(IReadOnlyList<OutputRoot> withinReach, CancellationToken cancellationToken);

    Task<RecordingStanding?> StandingAsync(RecordingId id, CancellationToken cancellationToken);
}
