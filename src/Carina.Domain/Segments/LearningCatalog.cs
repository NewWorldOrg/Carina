using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// One sound of the catalog as learning reads it: its entry, its length, and every stretch it is heard
/// in, placed in its programme beside the kind an answer on that recording gives it.
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
/// all. The answer given on the recording itself is set aside, so that no recording is read with the
/// answer it is learned or judged by, and so is the answer of a recording held out, so that no label
/// learned from stands on the answer the held-out recording is judged by: an entry its answer decided
/// falls back to what the rest of its stretches say, and a stretch its answer gave a kind loses it.
/// </summary>
public sealed class LearningCatalog
{
    private readonly ILookup<RecordingId, CatalogSound> soundsIn;

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
        this.links = links;
    }

    public static LearningCatalog Empty { get; } = new([], SeriesLinks.None);

    public IReadOnlyList<CatalogHeard> HeardBy(RecordingId recording, DateTime startedAt, RecordingId? heldOut = null)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return [.. soundsIn[recording].SelectMany(sound => Heard(sound, recording, startedAt, heldOut))];
    }

    /// <summary>
    /// Whether what <paramref name="recording"/> hears stands on an answer given on a stretch of
    /// <paramref name="answering"/>, another recording started no later than it.
    /// </summary>
    public bool HearsTheAnswerOf(RecordingId recording, DateTime startedAt, RecordingId answering)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(answering);

        return !recording.Equals(answering)
               && soundsIn[recording].Any(sound => sound.Occurrences.Any(placed => placed.Answer is not null
                                                                                   && placed.Occurrence.RecordingId.Equals(answering)
                                                                                   && placed.Occurrence.RecordingStartedAt <= startedAt));
    }

    private IEnumerable<CatalogHeard> Heard(CatalogSound sound, RecordingId recording, DateTime startedAt, RecordingId? heldOut)
    {
        PlacedOccurrence[] kept =
        [
            .. sound.Occurrences
                .Where(placed => placed.Occurrence.RecordingStartedAt <= startedAt)
                .Select(placed => SetsAside(placed, recording, heldOut) ? placed with { Answer = null } : placed),
        ];

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
                pattern.Estimate.Confidence));
    }

    private static bool SetsAside(PlacedOccurrence placed, RecordingId recording, RecordingId? heldOut)
        => placed.Occurrence.RecordingId.Equals(recording) || placed.Occurrence.RecordingId.Equals(heldOut);
}
