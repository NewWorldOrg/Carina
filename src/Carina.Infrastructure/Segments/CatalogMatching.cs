using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// What matching one recording against the catalog found, and what it changes in the catalog.
/// </summary>
public sealed record CatalogMatched(RecordingId Recording, int Matches, CatalogChange Change);

/// <summary>
/// Matches the learning data of one recording against every catalog entry, against the stretches of the
/// recordings started within the window around it that no entry explains yet, and against itself, one
/// recording's data read at a time; then works out what the matches change in the catalog. Estimates a kept
/// entry again from the stretches it has. What is read beside the matches is the
/// <see cref="CatalogMatchingDomainService"/>'s to say. The counting is done on the
/// <see cref="LowestPriorityScheduler"/>, so that it gives way to everything else the machine does. Nothing
/// is written here.
/// </summary>
public sealed class CatalogMatching(
    ICatalogWorklist worklist,
    ICatalogRepository catalog,
    CatalogMatchingDomainService knowing,
    LowestPriorityScheduler lowest)
{
    public async Task<CatalogMatched> MatchAsync(
        LearningExtraction heard,
        TimeSpan around,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(heard);

        RecordingId id = heard.RecordingId;
        SoundTrack sound = await worklist.SoundAsync(id, cancellationToken);
        SegmentMatcher matcher = await lowest.Run(() => SegmentMatcher.For(sound), cancellationToken);
        IReadOnlyList<CatalogEntry> entries = await catalog.ListEntriesAsync(cancellationToken);
        List<SoundMatch> matches = await lowest.Run(
            () => entries.SelectMany(entry => matcher.Against(new EntrySound(entry.Id), entry.Frames(), [])).ToList(),
            cancellationToken);

        matches.AddRange(await AgainstRecordingsAsync(matcher, heard, around, cancellationToken));
        matches.AddRange(await lowest.Run(() => matcher.AgainstItself(new RecordedSound(id)), cancellationToken));

        CatalogKnowledge known = await knowing.KnowledgeOfMatchesAsync(id, matches, cancellationToken);

        return new CatalogMatched(id, matches.Count, await lowest.Run(() => Catalog.Absorb(id, sound, matches, known, at), cancellationToken));
    }

    /// <summary>
    /// What estimating <paramref name="entry"/> again from every stretch it has changes in the catalog.
    /// </summary>
    public async Task<CatalogChange> ReestimateAsync(CatalogEntry entry, DateTime at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        CatalogKnowledge known = await knowing.KnowledgeOfEntryAsync(entry, cancellationToken);

        return await lowest.Run(() => Catalog.Reestimate(entry, known, at), cancellationToken);
    }

    private async Task<List<SoundMatch>> AgainstRecordingsAsync(
        SegmentMatcher matcher,
        LearningExtraction heard,
        TimeSpan around,
        CancellationToken cancellationToken)
    {
        List<SoundMatch> matches = [];

        foreach (CatalogNeighbour other in await knowing.AroundAsync(heard, around, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            SoundTrack there = await worklist.SoundAsync(other.Recording, cancellationToken);
            SoundSpan[] explaining = [.. other.Explained];

            matches.AddRange(await lowest.Run(() => matcher.Against(new RecordedSound(other.Recording), there.Fingerprints, explaining), cancellationToken));
        }

        return matches;
    }
}
