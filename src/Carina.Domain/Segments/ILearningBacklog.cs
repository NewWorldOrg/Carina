using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public sealed record BackloggedRecording(Recording Recording, LearningExtraction? Record);

public interface ILearningBacklog
{
    Task<IReadOnlyList<BackloggedRecording>> AwaitingAsync(
        IReadOnlyList<OutputRoot> withinReach,
        int skip,
        int atMost,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LearningExtraction>> UncaptionedAsync(int skip, int atMost, CancellationToken cancellationToken);
}
