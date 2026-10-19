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
/// from those (<see cref="CatalogKinds"/>), and one with fewer than two of them by then is not heard at
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

        return [.. soundsIn[recording].SelectMany(sound => Heard(sound, recording, startedAt))];
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
    /// <paramref name="context"/> remembering its earlier episodes without the stretches they were taken to hold
    /// over a stretch of <paramref name="entries"/>, so that what was set aside does not come back through what
    /// was detected in them.
    /// </summary>
    public SegmentContext Forgetting(SegmentContext context, IReadOnlySet<CatalogEntryId> entries)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entries);

        return entries.Count is 0 ? context : context.Remembering([.. context.Past.Select(episode => episode.Without(StretchesOf(episode.Recording, entries)))]);
    }

    private IEnumerable<CatalogHeard> Heard(CatalogSound sound, RecordingId recording, DateTime startedAt)
    {
        PlacedOccurrence[] kept = [.. sound.Occurrences.Where(placed => placed.Occurrence.RecordingStartedAt <= startedAt)];

        if (kept.Length < CatalogEntry.FewestOccurrences)
        {
            return [];
        }

        CatalogPattern pattern = CatalogKinds.Estimate(sound.Length, kept, links);
        SegmentKind? kind = CatalogHeard.CountsAs(pattern.Estimate.Kind);

        return kept
            .Select((placed, at) => (placed.Occurrence, Override: pattern.Overrides[at]))
            .Where(pair => pair.Occurrence.RecordingId.Equals(recording))
            .Select(pair => new CatalogHeard(
                pair.Occurrence.StartsAt,
                pair.Occurrence.EndsAt,
                pair.Override ?? kind,
                kept.Length,
                pattern.SeriesCount,
                pattern.StationCount,
                pattern.Estimate.Confidence,
                sound.Id));
    }
}
