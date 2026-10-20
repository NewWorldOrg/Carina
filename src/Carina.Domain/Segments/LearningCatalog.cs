using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// One sound of the catalog as learning reads it: its entry, its length, and every stretch it is heard
/// in, placed in its programme.
/// </summary>
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

/// <summary>
/// The catalog a recording is read with when learning: what it would have heard when it was made. Only
/// the stretches of recordings started no later than it count, so each entry it hears is estimated again
/// from those (<see cref="CatalogKinds"/>), beside the other entries heard in its recordings as they would
/// have been estimated then, and one with fewer than two of them by then is not heard at
/// all. Every stretch heard says which entry it is a stretch of, so that the entries set aside when a recording is
/// judged (<see cref="SetAsideFor"/>) can be taken out of what another recording hears.
/// </summary>
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

    /// <summary>
    /// What <paramref name="recording"/>, started at <paramref name="startedAt"/>, would have heard of the catalog
    /// when it was made.
    /// </summary>
    public IReadOnlyList<CatalogHeard> HeardBy(RecordingId recording, DateTime startedAt)
    {
        ArgumentNullException.ThrowIfNull(recording);

        Estimates then = new(soundsIn, links, startedAt);

        return [.. soundsIn[recording].SelectMany(sound => Heard(sound, recording, then))];
    }

    /// <summary>
    /// What the catalog hears in <paramref name="recording"/> now, every stretch it has counted.
    /// </summary>
    public IReadOnlyList<CatalogHeard> HeardNow(RecordingId recording) => HeardBy(recording, DateTime.MaxValue);

    /// <summary>
    /// The entries set aside when <paramref name="recording"/>, started at <paramref name="startedAt"/>, is judged:
    /// every entry heard in it, so that what it is judged against is not what it is detected with, and every entry
    /// first heard in a recording started after it, which it could not have known.
    /// </summary>
    public IReadOnlySet<CatalogEntryId> SetAsideFor(RecordingId recording, DateTime startedAt)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return new HashSet<CatalogEntryId>(
        [
            .. soundsIn[recording].Select(sound => sound.Id),
            .. firstHeard.Where(sound => sound.FirstHeard > startedAt).Select(sound => sound.Entry),
        ]);
    }

    /// <summary>
    /// The stretches of <paramref name="entries"/> heard in <paramref name="recording"/>, every one the catalog has.
    /// </summary>
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

    /// <summary>
    /// <paramref name="context"/> remembering its earlier episodes without the part of what they were taken to hold
    /// that lies over a stretch of <paramref name="entries"/>, so that what was set aside does not come back through
    /// what was detected in them, and the rest of what was detected in them stays.
    /// </summary>
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

    /// <summary>
    /// The sounds as they would have been estimated at one time, each worked out once: from their stretches of
    /// recordings started no later than then, and, for one that may carry on an opening or an ending, beside the
    /// sounds heard in its recordings.
    /// </summary>
    private sealed class Estimates(ILookup<RecordingId, CatalogSound> soundsIn, SeriesLinks links, DateTime startedAt)
    {
        private readonly Dictionary<CatalogEntryId, (PlacedOccurrence[] Kept, CatalogPattern Pattern)?> own = [];

        public (PlacedOccurrence[] Kept, CatalogPattern Pattern)? Carried(CatalogSound sound)
        {
            if (Own(sound) is not (PlacedOccurrence[] kept, CatalogPattern pattern))
            {
                return null;
            }

            if (!CatalogKinds.MayCarryOn(pattern))
            {
                return (kept, pattern);
            }

            CatalogSurroundings around = new(
            [
                .. kept
                    .Select(placed => placed.Occurrence.RecordingId)
                    .Distinct()
                    .SelectMany(recording => soundsIn[recording].SelectMany(heard => StretchesIn(heard, recording))),
            ]);

            return (kept, CatalogKinds.CarryOn(sound.Length, kept, pattern, around));
        }

        private IEnumerable<ThemeStretch> StretchesIn(CatalogSound sound, RecordingId recording)
            => Own(sound) is (PlacedOccurrence[] kept, CatalogPattern pattern)
                ? CatalogKinds.Stretches(kept, pattern).Where(stretch => stretch.Recording.Equals(recording))
                : [];

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
