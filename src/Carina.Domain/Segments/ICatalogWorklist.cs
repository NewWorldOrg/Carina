using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public interface ICatalogWorklist
{
    Task<LearningExtraction?> NextAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LearningExtraction>> AroundAsync(
        RecordingId heard,
        DateTime startedAt,
        TimeSpan within,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<RecordingId, ProgrammeCopy>> ProgrammesAsync(
        IReadOnlyCollection<RecordingId> recordings,
        CancellationToken cancellationToken);

    Task<SoundTrack> SoundAsync(RecordingId recording, CancellationToken cancellationToken);
}
