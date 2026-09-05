using Carina.Domain.Recordings;

namespace Carina.Domain.Library;

public sealed record LibraryRecordingPage(IReadOnlyList<LibraryRecordingSummary> Rows, RecordingCursor? Next);

public interface IRecordingLibraryRepository
{
    Task<LibraryRecordingPage> SearchAsync(RecordingSearchCriteria criteria, CancellationToken cancellationToken);

    Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken);

    Task<int> DeleteAsync(RecordingId id, CancellationToken cancellationToken);
}
