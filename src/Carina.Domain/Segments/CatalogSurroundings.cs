using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public enum ThemePart
{
    None = 1,

    Opening = 2,

    Ending = 3,

    Piece = 4,
}

public sealed record ThemeStretch(CatalogEntryId Entry, RecordingId Recording, SoundSpan Span, ThemePart Part, SegmentKind? CountsAs)
{
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

    public IReadOnlyList<ThemeStretch> In(RecordingId recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return [.. heard[recording]];
    }

    public CatalogSurroundings Replacing(IReadOnlyCollection<CatalogEntryId> entries, IEnumerable<ThemeStretch> replaced)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(replaced);

        return new CatalogSurroundings([.. stretches.Where(stretch => !entries.Contains(stretch.Entry)), .. replaced]);
    }
}
