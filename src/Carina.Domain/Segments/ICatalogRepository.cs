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
    /// The entries named, with their fingerprints, read so that keeping them later refuses an entry whose row
    /// changed since (<see cref="KeepAsync"/>).
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
    /// Every stretch heard in the recordings named, whichever entry it is of, with what it is to an opening or an
    /// ending as its entry is now taken (<see cref="CatalogKinds.PartOf"/>).
    /// </summary>
    Task<CatalogSurroundings> SurroundingsAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken);

    /// <summary>
    /// The recordings each opening, ending and fixed part of a programme links into one series, one group per
    /// entry: those its stretches that link series are heard in (<see cref="CatalogKinds.LinksSeries"/>).
    /// </summary>
    Task<IReadOnlyList<IReadOnlyList<RecordingId>>> SoundSharersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The entry first seen earliest of those that <see cref="CatalogEntry.AwaitsReestimation"/>, with its
    /// fingerprint, read as <see cref="EntriesAsync"/> reads; null when none does.
    /// </summary>
    Task<CatalogEntry?> NextToReestimateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the change whole or not at all: the entries started and the stretches found are added, the
    /// entries estimated again and the stretches taken for another kind are written as they now stand, and
    /// every other entry heard in a recording an entry relinking is heard in is put back to
    /// <see cref="CatalogKinds.Unsettled"/>, to be estimated again. An entry estimated again whose row changed
    /// since it was read, such as one put back meanwhile to be estimated again, is left as it now stands, and so are the
    /// stretches taken for another kind with it; it waits to be estimated again. An entry estimated again is one
    /// read through this repository, by <see cref="EntriesAsync"/> or <see cref="NextToReestimateAsync"/>.
    /// </summary>
    Task KeepAsync(CatalogChange change, CancellationToken cancellationToken);
}
