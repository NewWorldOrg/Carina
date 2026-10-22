using Carina.Domain.Segments;

namespace Carina.Domain.Encodings;

/// <summary>
/// Where an encode takes the chapters it embeds from, decided as playback decides which stretches a recording
/// is played with (<see cref="SegmentStanding"/>): the detection by the version of the model in use when there
/// is one, and nothing when there is none.
/// </summary>
/// <remarks>
/// Only stretches that mark something besides the programme are embedded. A detection that gives only the
/// programme, or one thrown away, embeds nothing, so the encode is handed no chapters. A stretch the artefact
/// does not play is not embedded.
/// </remarks>
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

    /// <summary>
    /// The stretches embedded, in the recording's own time, which is the time the chapters handed to the
    /// encode are written in.
    /// </summary>
    public IReadOnlyList<SegmentSpan> Embedded(PlaybackFileClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return Standing.Marks ? [.. Standing.Played.Where(stretch => clock.Placed(stretch) is not null)] : [];
    }

    /// <summary>
    /// The chapters embedded, on the artefact's own clock, as the ledger keeps them.
    /// </summary>
    public IReadOnlyList<ChapterSegment> Chapters(PlaybackFileClock clock)
        => [.. Embedded(clock).Select(clock.Placed).OfType<SegmentSpan>().Select(stretch => new ChapterSegment(stretch.Starts, stretch.Ends, KindOf(stretch.Kind)))];

    /// <summary>
    /// What the ledger keeps of where the embedded chapters came from, decided at <paramref name="at"/>.
    /// </summary>
    public ChapterReading Reading(DateTime at)
        => new(ChapterDetectorName.Segments, Verdict, Standing.BreakShare, at, Standing.ModelVersion);

    /// <summary>
    /// The kind of chapter a stretch is embedded as, which is its own kind.
    /// </summary>
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
