using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public interface ICatalogRepository
{
    Task<IReadOnlyList<CatalogEntry>> ListEntriesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogEntry>> EntriesAsync(IReadOnlyCollection<CatalogEntryId> entries, CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogOccurrence>> OccurrencesOfAsync(IReadOnlyCollection<CatalogEntryId> entries, CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogOccurrence>> OccurrencesInAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken);

    Task<CatalogSurroundings> SurroundingsAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken);

    Task<IReadOnlyList<IReadOnlyList<RecordingId>>> SoundSharersAsync(CancellationToken cancellationToken);

    Task<CatalogEntry?> NextToReestimateAsync(CancellationToken cancellationToken);

    Task KeepAsync(CatalogChange change, CancellationToken cancellationToken);
}
