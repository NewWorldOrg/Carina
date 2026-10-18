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
/// recording's data read at a time; then works out what the matches change in the catalog. Nothing is
/// written here.
/// </summary>
public sealed class CatalogMatching(ICatalogWorklist worklist, ICatalogRepository catalog)
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
        SegmentMatcher matcher = SegmentMatcher.For(sound);
        IReadOnlyList<CatalogEntry> entries = await catalog.ListEntriesAsync(cancellationToken);
        List<SoundMatch> matches = [.. entries.SelectMany(entry => matcher.Against(new EntrySound(entry.Id), entry.Frames(), []))];

        matches.AddRange(await AgainstRecordingsAsync(matcher, heard, around, cancellationToken));
        matches.AddRange(matcher.AgainstItself(new RecordedSound(id)));

        HashSet<CatalogEntryId> named = [.. matches.Select(match => match.Other).OfType<EntrySound>().Select(entry => entry.Entry)];
        IReadOnlyList<CatalogOccurrence> occurrences = await catalog.OccurrencesOfAsync(named, cancellationToken);
        RecordingId[] involved =
        [
            .. matches.Select(match => match.Other).OfType<RecordedSound>().Select(other => other.Recording)
                .Concat(occurrences.Select(occurrence => occurrence.RecordingId))
                .Append(id)
                .Distinct(),
        ];
        CatalogKnowledge known = new(
            [.. entries.Where(entry => named.Contains(entry.Id))],
            occurrences,
            await worklist.ProgrammesAsync(involved, cancellationToken),
            SeriesLinks.SharingSound(await catalog.FixtureSharersAsync(cancellationToken)));

        return new CatalogMatched(id, matches.Count, Catalog.Absorb(id, sound, matches, known, at));
    }

    private async Task<List<SoundMatch>> AgainstRecordingsAsync(
        SegmentMatcher matcher,
        LearningExtraction heard,
        TimeSpan around,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LearningExtraction> others = await worklist.AroundAsync(heard.RecordingId, heard.Programme.RecordingStartedAt, around, cancellationToken);
        ILookup<RecordingId, SoundSpan> explained = (await catalog.OccurrencesInAsync([.. others.Select(other => other.RecordingId)], cancellationToken))
            .ToLookup(occurrence => occurrence.RecordingId, occurrence => occurrence.Span);
        List<SoundMatch> matches = [];

        foreach (LearningExtraction other in others)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SoundTrack there = await worklist.SoundAsync(other.RecordingId, cancellationToken);

            matches.AddRange(matcher.Against(new RecordedSound(other.RecordingId), there.Fingerprints, [.. explained[other.RecordingId]]));
        }

        return matches;
    }
}
