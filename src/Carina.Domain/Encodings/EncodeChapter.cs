namespace Carina.Domain.Encodings;

/// <summary>
/// One chapter of one job's artefact, as the ledger holds it: on the artefact's own clock, because
/// that is the clock a player of the artefact is on. It belongs to the job rather than to the
/// recording, so a recording encoded twice carries the reading each run made of it and a player is
/// answered with the one belonging to the artefact it was handed. Where the chapter sits among its
/// neighbours is kept as its own number rather than left to the order rows come back in.
/// </summary>
public sealed class EncodeChapter
{
    public const int FirstOrdinal = 0;

    private EncodeChapter()
    {
    }

    public EncodeChapterId Id { get; private set; } = null!;

    public EncodeJobId JobId { get; private set; } = null!;

    public int Ordinal { get; private set; }

    public TimeSpan StartsAt { get; private set; }

    public TimeSpan EndsAt { get; private set; }

    public ChapterKind Kind { get; private set; }

    public ChapterSegment Segment => new(StartsAt, EndsAt, Kind);

    public static EncodeChapter Mark(EncodeChapterId id, EncodeJobId jobId, int ordinal, ChapterSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        return Rehydrate(id, jobId, ordinal, segment.Starts, segment.Ends, segment.Kind);
    }

    public static IReadOnlyList<EncodeChapter> Mark(EncodeJobId jobId, IReadOnlyList<ChapterSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        return [.. segments.Select((segment, ordinal) => Mark(EncodeChapterId.New(), jobId, FirstOrdinal + ordinal, segment))];
    }

    public static EncodeChapter Rehydrate(
        EncodeChapterId id,
        EncodeJobId jobId,
        int ordinal,
        TimeSpan startsAt,
        TimeSpan endsAt,
        ChapterKind kind)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentOutOfRangeException.ThrowIfLessThan(ordinal, FirstOrdinal);
        ArgumentOutOfRangeException.ThrowIfLessThan(startsAt, TimeSpan.Zero, nameof(startsAt));

        if (endsAt <= startsAt)
        {
            throw new ArgumentOutOfRangeException(nameof(endsAt), endsAt, "A chapter ends after it starts.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A chapter is one of the kinds named here.");
        }

        return new EncodeChapter
        {
            Id = id,
            JobId = jobId,
            Ordinal = ordinal,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Kind = kind,
        };
    }
}
