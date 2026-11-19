using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

public sealed record CatalogMatched(RecordingId Recording, int Matches, CatalogChange Change);

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
