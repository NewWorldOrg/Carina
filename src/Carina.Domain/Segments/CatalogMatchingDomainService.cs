using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public sealed record CatalogNeighbour(RecordingId Recording, IReadOnlyList<SoundSpan> Explained);

public sealed class CatalogMatchingDomainService(ICatalogWorklist worklist, ICatalogRepository catalog)
{
    public async Task<IReadOnlyList<CatalogNeighbour>> AroundAsync(
        LearningExtraction heard,
        TimeSpan around,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(heard);

        IReadOnlyList<LearningExtraction> others = await worklist.AroundAsync(heard.RecordingId, heard.Programme.RecordingStartedAt, around, cancellationToken);
        ILookup<RecordingId, SoundSpan> explained = (await catalog.OccurrencesInAsync([.. others.Select(other => other.RecordingId)], cancellationToken))
            .ToLookup(occurrence => occurrence.RecordingId, occurrence => occurrence.Span);

        return [.. others.Select(other => new CatalogNeighbour(other.RecordingId, [.. explained[other.RecordingId]]))];
    }

    public async Task<CatalogKnowledge> KnowledgeOfMatchesAsync(
        RecordingId id,
        IReadOnlyList<SoundMatch> matches,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(matches);

        HashSet<CatalogEntryId> named = [.. matches.Select(match => match.Other).OfType<EntrySound>().Select(entry => entry.Entry)];
        IReadOnlyList<CatalogOccurrence> occurrences = await catalog.OccurrencesOfAsync(named, cancellationToken);
        RecordingId[] recorded = [.. matches.Select(match => match.Other).OfType<RecordedSound>().Select(other => other.Recording).Append(id).Distinct()];
        RecordingId[] involved = [.. recorded.Concat(occurrences.Select(occurrence => occurrence.RecordingId)).Distinct()];
        IReadOnlyList<CatalogEntry> kept = named.Count is 0 ? [] : await catalog.EntriesAsync(named, cancellationToken);
        HashSet<CatalogEntryId> ofOneSeries = [.. kept.Where(entry => entry.SeriesCount == 1).Select(entry => entry.Id)];
        RecordingId[] beside =
        [
            .. recorded.Concat(occurrences.Where(occurrence => ofOneSeries.Contains(occurrence.EntryId)).Select(occurrence => occurrence.RecordingId)).Distinct(),
        ];

        return await KnowingAsync(kept, occurrences, involved, beside, cancellationToken);
    }

    public async Task<CatalogKnowledge> KnowledgeOfEntryAsync(CatalogEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        IReadOnlyList<CatalogOccurrence> occurrences = await catalog.OccurrencesOfAsync([entry.Id], cancellationToken);
        RecordingId[] involved = [.. occurrences.Select(occurrence => occurrence.RecordingId).Distinct()];

        return await KnowingAsync([entry], occurrences, involved, entry.SeriesCount == 1 ? involved : [], cancellationToken);
    }

    public async Task<IReadOnlyList<RecordingId>> HeardAnewAsync(CatalogChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        IReadOnlyList<CatalogOccurrence> heard = change.Recast.Count is 0 ? [] : await catalog.OccurrencesOfAsync(change.Recast, cancellationToken);

        return [.. heard.Concat(change.Overridden).Select(occurrence => occurrence.RecordingId).Distinct()];
    }

    private async Task<CatalogKnowledge> KnowingAsync(
        IReadOnlyList<CatalogEntry> entries,
        IReadOnlyList<CatalogOccurrence> occurrences,
        RecordingId[] involved,
        RecordingId[] beside,
        CancellationToken cancellationToken)
        => new(
            entries,
            occurrences,
            await worklist.ProgrammesAsync(involved, cancellationToken),
            SeriesLinks.SharingSound(await catalog.SoundSharersAsync(cancellationToken)),
            beside.Length is 0 ? CatalogSurroundings.None : await catalog.SurroundingsAsync(beside, cancellationToken));
}
