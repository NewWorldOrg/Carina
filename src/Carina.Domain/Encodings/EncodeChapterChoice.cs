using Carina.Domain.Segments;

namespace Carina.Domain.Encodings;

/// <summary>
/// Where an encode takes the chapters it embeds from, decided as playback decides which stretches a recording
/// is played with (<see cref="SegmentStanding"/>): the detection by the version of the model in use when there
/// is one, and nothing when learning is on and there is none. While learning is off and the version in use has
/// not detected the recording, the reader of the source the machine is set up with is asked instead
/// (<see cref="AsksTheReader"/>).
/// </summary>
/// <remarks>
/// Only stretches that mark something besides the programme are embedded. A detection that gives only the
/// programme, or one thrown away, embeds nothing, as a reading of the source that marked
/// nothing does, so the encode is handed no chapters. A stretch the artefact does not play is not embedded.
/// </remarks>
public sealed record EncodeChapterChoice
{
    private EncodeChapterChoice(bool asksTheReader, int modelVersion, SegmentStanding standing)
    {
        AsksTheReader = asksTheReader;
        ModelVersion = modelVersion;
        Standing = standing;
    }

    public bool AsksTheReader { get; }

    public int ModelVersion { get; }

    public SegmentStanding Standing { get; }

    public static EncodeChapterChoice Of(bool learning, int modelVersion, SegmentStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);
        ArgumentOutOfRangeException.ThrowIfLessThan(modelVersion, SegmentModelVersion.FirstNumber);

        if (standing.Detection is { } detection && detection.ModelVersion != modelVersion)
        {
            throw new ArgumentException("A recording stands with the detection by the version in use.", nameof(standing));
        }

        return new EncodeChapterChoice(!learning && standing.Source is SegmentSource.None, modelVersion, standing);
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
    {
        if (AsksTheReader)
        {
            throw new InvalidOperationException("The reader of the source answers for chapters it was asked for.");
        }

        return new ChapterReading(
            ChapterDetectorName.Segments,
            Verdict,
            Standing.BreakShare,
            at,
            Standing.ModelVersion);
    }

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
