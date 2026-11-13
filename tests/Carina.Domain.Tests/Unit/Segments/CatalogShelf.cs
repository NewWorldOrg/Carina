using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Unit.Segments;

/// <summary>
/// The catalog held in memory for the domain services that read it: its entries, their stretches and the recordings
/// sharing a sound, as a test lays them, and the recordings whose surroundings were asked for.
/// </summary>
internal sealed class CatalogShelf : ICatalogRepository
{
    public List<CatalogEntry> Entries { get; } = [];

    public List<CatalogOccurrence> Occurrences { get; } = [];

    public List<RecordingId> AskedAround { get; } = [];

    public List<IReadOnlyList<RecordingId>> Sharers { get; } = [];

    public int EntriesAsked { get; private set; }

    public Task<IReadOnlyList<CatalogEntry>> ListEntriesAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CatalogEntry>>([.. Entries]);

    public Task<IReadOnlyList<CatalogEntry>> EntriesAsync(IReadOnlyCollection<CatalogEntryId> entries, CancellationToken cancellationToken)
    {
        EntriesAsked++;

        return Task.FromResult<IReadOnlyList<CatalogEntry>>([.. Entries.Where(entry => entries.Contains(entry.Id))]);
    }

    public Task<IReadOnlyList<CatalogOccurrence>> OccurrencesOfAsync(IReadOnlyCollection<CatalogEntryId> entries, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CatalogOccurrence>>([.. Occurrences.Where(occurrence => entries.Contains(occurrence.EntryId))]);

    public Task<IReadOnlyList<CatalogOccurrence>> OccurrencesInAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CatalogOccurrence>>([.. Occurrences.Where(occurrence => recordings.Contains(occurrence.RecordingId))]);

    public Task<CatalogSurroundings> SurroundingsAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken)
    {
        AskedAround.AddRange(recordings);

        return Task.FromResult(CatalogSurroundings.None);
    }

    public Task<IReadOnlyList<IReadOnlyList<RecordingId>>> SoundSharersAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<IReadOnlyList<RecordingId>>>([.. Sharers]);

    public Task<CatalogEntry?> NextToReestimateAsync(CancellationToken cancellationToken)
        => Task.FromResult<CatalogEntry?>(null);

    public Task KeepAsync(CatalogChange change, CancellationToken cancellationToken) => Task.CompletedTask;
}
