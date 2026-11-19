using Carina.Domain.Segments;

namespace Carina.Domain.Encodings;

public sealed record EncodeChapterChoice
{
    private EncodeChapterChoice(int modelVersion, SegmentStanding standing)
    {
        ModelVersion = modelVersion;
        Standing = standing;
    }

    public int ModelVersion { get; }

    public SegmentStanding Standing { get; }

    public static EncodeChapterChoice Of(int modelVersion, SegmentStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);
        ArgumentOutOfRangeException.ThrowIfLessThan(modelVersion, SegmentModelVersion.FirstNumber);

        if (standing.Detection is { } detection && detection.ModelVersion != modelVersion)
        {
            throw new ArgumentException("A recording stands with the detection by the version in use.", nameof(standing));
        }

        return new EncodeChapterChoice(modelVersion, standing);
    }

    public IReadOnlyList<SegmentSpan> Embedded(PlaybackFileClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return Standing.Marks ? [.. Standing.Played.Where(stretch => clock.Placed(stretch) is not null)] : [];
    }

    public IReadOnlyList<ChapterSegment> Chapters(PlaybackFileClock clock)
        => [.. Embedded(clock).Select(clock.Placed).OfType<SegmentSpan>().Select(stretch => new ChapterSegment(stretch.Starts, stretch.Ends, KindOf(stretch.Kind)))];

    public ChapterReading Reading(DateTime at)
        => new(ChapterDetectorName.Segments, Verdict, Standing.BreakShare, at, Standing.ModelVersion);

    public static ChapterKind KindOf(SegmentKind kind) => kind switch
    {
        SegmentKind.Programme => ChapterKind.Programme,
        SegmentKind.Break => ChapterKind.Break,
        SegmentKind.Opening => ChapterKind.Opening,
        SegmentKind.Ending => ChapterKind.Ending,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "An artefact's chapters are the programme, a break, an opening or an ending."),
    };

    private ChapterVerdict Verdict => Standing.State switch
    {
        SegmentStandingState.Marked => ChapterVerdict.Marked,
        SegmentStandingState.NothingFound => ChapterVerdict.NothingFound,
        SegmentStandingState.Discarded => ChapterVerdict.Discarded,
        _ => ChapterVerdict.NotAsked,
    };
}
