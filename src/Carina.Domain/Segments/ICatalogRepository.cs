using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// Where the catalog is kept: its entries, one row each, and their stretches. Nothing is removed from it.
/// </summary>
public interface ICatalogRepository
{
    /// <summary>
    /// Every entry, with its fingerprint.
    /// </summary>
    Task<IReadOnlyList<CatalogEntry>> ListEntriesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The entries named, with their fingerprints.
    /// </summary>
    Task<IReadOnlyList<CatalogEntry>> EntriesAsync(IReadOnlyCollection<CatalogEntryId> entries, CancellationToken cancellationToken);

    /// <summary>
    /// Every stretch of the entries named.
    /// </summary>
    Task<IReadOnlyList<CatalogOccurrence>> OccurrencesOfAsync(IReadOnlyCollection<CatalogEntryId> entries, CancellationToken cancellationToken);

    /// <summary>
    /// Every stretch heard in the recordings named, whichever entry it is of.
    /// </summary>
    Task<IReadOnlyList<CatalogOccurrence>> OccurrencesInAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken);

    /// <summary>
    /// The recordings each fixed part of a programme is heard in, one group per entry.
    /// </summary>
    Task<IReadOnlyList<IReadOnlyList<RecordingId>>> FixtureSharersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the change whole or not at all: the entries started and the stretches found are added, the
    /// entries estimated again and the stretches taken for another kind are written as they now stand.
    /// </summary>
    Task KeepAsync(CatalogChange change, CancellationToken cancellationToken);
}
