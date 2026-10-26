using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// What a stretch heard in a recording is to an opening or an ending cut into pieces: the opening or the ending
/// itself, a piece of one series that can carry it on, or nothing to it.
/// </summary>
public enum ThemePart
{
    None = 1,

    Opening = 2,

    Ending = 3,

    Piece = 4,
}

/// <summary>
/// A stretch of a catalog entry heard in a recording, what it is to an opening or an ending there, and the kind it
/// counts as there (<see cref="CatalogHeard.KindOf"/>), none when it counts as nothing.
/// </summary>
public sealed record ThemeStretch(CatalogEntryId Entry, RecordingId Recording, SoundSpan Span, ThemePart Part, SegmentKind? CountsAs)
{
    /// <summary>
    /// A stretch of an entry taken for <paramref name="estimate"/> in <paramref name="seriesCount"/> series, which alone
    /// is taken for <paramref name="kindOverride"/>.
    /// </summary>
    public static ThemeStretch Of(
        CatalogEntryId entry,
        RecordingId recording,
        SoundSpan span,
        CatalogEstimate estimate,
        int seriesCount,
        SegmentKind? kindOverride)
    {
        ArgumentNullException.ThrowIfNull(estimate);

        return new ThemeStretch(
            entry,
            recording,
            span,
            CatalogKinds.PartOf(estimate, seriesCount, kindOverride),
            CatalogHeard.KindOf(kindOverride, estimate.Kind));
    }
}

/// <summary>
/// The stretches heard in some recordings, whichever entries they are of, what each is to an opening or an ending,
/// and the kind each counts as: what an entry is estimated beside when they may settle it
/// (<see cref="CatalogKinds.Settle"/>). A recording not handed over hears nothing beside the entry.
/// </summary>
public sealed class CatalogSurroundings
{
    private readonly IReadOnlyList<ThemeStretch> stretches;

    private readonly ILookup<RecordingId, ThemeStretch> heard;

    public CatalogSurroundings(IEnumerable<ThemeStretch> stretches)
    {
        ArgumentNullException.ThrowIfNull(stretches);

        this.stretches = [.. stretches];

        if (this.stretches.Any(stretch => stretch is null))
        {
            throw new ArgumentException("Every stretch is handed over.", nameof(stretches));
        }

        heard = this.stretches.OrderBy(stretch => stretch.Span.Starts).ThenBy(stretch => stretch.Span.Ends).ToLookup(stretch => stretch.Recording);
    }

    public static CatalogSurroundings None { get; } = new([]);

    /// <summary>
    /// The stretches heard in <paramref name="recording"/>, the earliest first.
    /// </summary>
    public IReadOnlyList<ThemeStretch> In(RecordingId recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return [.. heard[recording]];
    }

    /// <summary>
    /// These surroundings with the stretches of <paramref name="entries"/> put in place of the ones they had.
    /// </summary>
    public CatalogSurroundings Replacing(IReadOnlyCollection<CatalogEntryId> entries, IEnumerable<ThemeStretch> replaced)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(replaced);

        return new CatalogSurroundings([.. stretches.Where(stretch => !entries.Contains(stretch.Entry)), .. replaced]);
    }
}
