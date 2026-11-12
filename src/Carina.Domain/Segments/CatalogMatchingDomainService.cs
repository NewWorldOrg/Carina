using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// A recording started near the one matched against the catalog, with the stretches of it an entry already explains,
/// which the matching passes over.
/// </summary>
public sealed record CatalogNeighbour(RecordingId Recording, IReadOnlyList<SoundSpan> Explained);

/// <summary>
/// Decides what matching a recording against the catalog reads beside the entries, looking up the catalog and the
/// recordings read for their learning data. A recording is matched against those started within the window around it,
/// passing over the stretches of each that an entry already explains. Putting the matches into the catalog reads the
/// entries they name with every stretch those have, the programmes of every recording involved, how series are linked,
/// and the stretches heard around the entries in the recordings matched, the recording itself and, for an entry heard
/// in one series only, every recording it is heard in, which may settle it as a sponsor's advert or as a piece of an
/// opening or an ending (<see cref="CatalogKinds.Settle"/>). Once a match or an estimate changes the catalog, every
/// recording that hears an entry whose kind or confidence changed, or a stretch taken for another kind, is detected
/// again.
/// </summary>
public sealed class CatalogMatchingDomainService(ICatalogWorklist worklist, ICatalogRepository catalog)
{
    /// <summary>
    /// The recordings started within <paramref name="around"/> of the one <paramref name="heard"/>, each with the
    /// stretches of it an entry already explains.
    /// </summary>
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

    /// <summary>
    /// What putting <paramref name="matches"/> found in the recording <paramref name="id"/> into the catalog reads.
    /// </summary>
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

    /// <summary>
    /// What estimating <paramref name="entry"/> again from every stretch it has reads.
    /// </summary>
    public async Task<CatalogKnowledge> KnowledgeOfEntryAsync(CatalogEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        IReadOnlyList<CatalogOccurrence> occurrences = await catalog.OccurrencesOfAsync([entry.Id], cancellationToken);
        RecordingId[] involved = [.. occurrences.Select(occurrence => occurrence.RecordingId).Distinct()];

        return await KnowingAsync([entry], occurrences, involved, entry.SeriesCount == 1 ? involved : [], cancellationToken);
    }

    /// <summary>
    /// The recordings to detect again once <paramref name="change"/> is kept: every recording that hears an entry it
    /// recast, or a stretch it took for another kind, each once.
    /// </summary>
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
