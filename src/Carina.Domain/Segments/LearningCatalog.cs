using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public sealed record CatalogSound
{
    public CatalogSound(CatalogEntryId id, TimeSpan length, IReadOnlyList<PlacedOccurrence> occurrences)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero);

        if (occurrences.Any(placed => placed is null || !placed.Occurrence.EntryId.Equals(id)))
        {
            throw new ArgumentException("A sound is heard in stretches of its own entry.", nameof(occurrences));
        }

        Id = id;
        Length = length;
        Occurrences = [.. occurrences];
    }

    public CatalogEntryId Id { get; }

    public TimeSpan Length { get; }

    public IReadOnlyList<PlacedOccurrence> Occurrences { get; }
}

public sealed class LearningCatalog
{
    private readonly ILookup<RecordingId, CatalogSound> soundsIn;

    private readonly IReadOnlyList<(CatalogEntryId Entry, DateTime FirstHeard)> firstHeard;

    private readonly SeriesLinks links;

    public LearningCatalog(IReadOnlyList<CatalogSound> sounds, SeriesLinks links)
    {
        ArgumentNullException.ThrowIfNull(sounds);
        ArgumentNullException.ThrowIfNull(links);

        if (sounds.Any(sound => sound is null))
        {
            throw new ArgumentException("Every sound is handed over.", nameof(sounds));
        }

        soundsIn = sounds
            .SelectMany(sound => sound.Occurrences.Select(placed => placed.Occurrence.RecordingId).Distinct().Select(recording => (recording, sound)))
            .ToLookup(pair => pair.recording, pair => pair.sound);
        firstHeard =
        [
            .. sounds
                .Where(sound => sound.Occurrences.Count is not 0)
                .Select(sound => (sound.Id, sound.Occurrences.Min(placed => placed.Occurrence.RecordingStartedAt))),
        ];
        this.links = links;
    }

    public static LearningCatalog Empty { get; } = new([], SeriesLinks.None);

    public IReadOnlyList<CatalogHeard> HeardBy(RecordingId recording, DateTime startedAt)
    {
        ArgumentNullException.ThrowIfNull(recording);

        Estimates then = new(soundsIn, links, startedAt);

        return [.. soundsIn[recording].SelectMany(sound => Heard(sound, recording, then))];
    }

    public IReadOnlyList<CatalogHeard> HeardNow(RecordingId recording) => HeardBy(recording, DateTime.MaxValue);

    public IReadOnlySet<CatalogEntryId> SetAsideFor(RecordingId recording, DateTime startedAt)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return new HashSet<CatalogEntryId>(
        [
            .. soundsIn[recording].Select(sound => sound.Id),
            .. firstHeard.Where(sound => sound.FirstHeard > startedAt).Select(sound => sound.Entry),
        ]);
    }

    public IReadOnlyList<SoundSpan> StretchesOf(RecordingId recording, IReadOnlySet<CatalogEntryId> entries)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(entries);

        return
        [
            .. soundsIn[recording]
                .Where(sound => entries.Contains(sound.Id))
                .SelectMany(sound => sound.Occurrences)
                .Where(placed => placed.Occurrence.RecordingId.Equals(recording))
                .Select(placed => placed.Occurrence.Span),
        ];
    }

    public SegmentContext Forgetting(SegmentContext context, IReadOnlySet<CatalogEntryId> entries)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entries);

        return entries.Count is 0 ? context : context.Remembering([.. context.Past.Select(episode => episode.Without(StretchesOf(episode.Recording, entries)))]);
    }

    private static IEnumerable<CatalogHeard> Heard(CatalogSound sound, RecordingId recording, Estimates then)
    {
        if (then.Carried(sound) is not (PlacedOccurrence[] kept, CatalogPattern pattern))
        {
            return [];
        }

        return kept
            .Select((placed, at) => (placed.Occurrence, Override: pattern.Overrides[at]))
            .Where(pair => pair.Occurrence.RecordingId.Equals(recording))
            .Select(pair => new CatalogHeard(
                pair.Occurrence.StartsAt,
                pair.Occurrence.EndsAt,
                CatalogHeard.KindOf(pair.Override, pattern.Estimate.Kind),
                kept.Length,
                pattern.SeriesCount,
                pattern.StationCount,
                pattern.Estimate.Confidence,
                sound.Id));
    }

    private sealed class Estimates(ILookup<RecordingId, CatalogSound> soundsIn, SeriesLinks links, DateTime startedAt)
    {
        private readonly Dictionary<CatalogEntryId, (PlacedOccurrence[] Kept, CatalogPattern Pattern)?> own = [];

        private IReadOnlyDictionary<CatalogEntryId, CatalogPattern>? settled;

        public (PlacedOccurrence[] Kept, CatalogPattern Pattern)? Carried(CatalogSound sound)
        {
            if (Own(sound) is not (PlacedOccurrence[] kept, CatalogPattern pattern))
            {
                return null;
            }

            if (!CatalogKinds.MaySettle(pattern))
            {
                return (kept, pattern);
            }

            settled ??= Settled();

            return (kept, settled[sound.Id]);
        }

        private IReadOnlyDictionary<CatalogEntryId, CatalogPattern> Settled()
        {
            (CatalogSound Sound, CatalogPattern Pattern)[] heard =
            [
                .. soundsIn
                    .SelectMany(sounds => sounds)
                    .DistinctBy(sound => sound.Id)
                    .Select(sound => (sound, Own(sound)))
                    .Where(pair => pair.Item2 is not null)
                    .Select(pair => (new CatalogSound(pair.sound.Id, pair.sound.Length, pair.Item2!.Value.Kept), pair.Item2!.Value.Pattern)),
            ];

            return CatalogKinds.SettleTogether(
                [.. heard.Select(pair => pair.Sound)],
                heard.ToDictionary(pair => pair.Sound.Id, pair => pair.Pattern),
                CatalogSurroundings.None);
        }

        private (PlacedOccurrence[] Kept, CatalogPattern Pattern)? Own(CatalogSound sound)
        {
            if (own.TryGetValue(sound.Id, out (PlacedOccurrence[] Kept, CatalogPattern Pattern)? known))
            {
                return known;
            }

            PlacedOccurrence[] kept = [.. sound.Occurrences.Where(placed => placed.Occurrence.RecordingStartedAt <= startedAt)];
            (PlacedOccurrence[] Kept, CatalogPattern Pattern)? estimated = kept.Length < CatalogEntry.FewestOccurrences
                ? null
                : (kept, CatalogKinds.Estimate(sound.Length, kept, links));

            own[sound.Id] = estimated;

            return estimated;
        }
    }
}
