namespace Carina.Domain.Segments;

/// <summary>
/// What is wrong with the stretches sent as an answer, named by kind alone: nothing sent is repeated.
/// </summary>
public enum AnswerFault
{
    Gap = 1,

    Overlap = 2,

    OutsideTheRecording = 3,

    UnknownKind = 4,

    TooMany = 5,

    EndsBeforeItStarts = 6,
}

/// <summary>
/// One stretch as a person sent it, in seconds of the recording's own time, and its kind when it names one of
/// the kinds of segment.
/// </summary>
public sealed record SentStretch(double StartsAtSec, double EndsAtSec, SegmentKind? Kind);

/// <summary>
/// The stretches read from what was sent, or what is wrong with it.
/// </summary>
public sealed record AnswerReading(AnsweredStretches? Stretches, AnswerFault? Fault)
{
    public static AnswerReading Refused(AnswerFault fault) => new(null, fault);
}

/// <summary>
/// The stretches a person gives a recording, in its own time: each rounded to <see cref="Resolution"/>, of a
/// kind in <see cref="KindsTaken"/>, ending after it starts, together covering the recording from zero to its
/// length rounded the same way with no gap and no overlap, two of the same kind side by side made one, and no
/// more than <see cref="MostStretchesAnHour"/> an hour. An answer is not held to an order of kinds: the
/// sequence a detection keeps to is not asked of it. A stretch given carries no probability.
/// </summary>
/// <remarks>
/// The first fault found is the one answered: stretch by stretch in the order sent, its kind first, then
/// whether it ends after it starts, then whether it lies within the recording; then, in the order the
/// stretches start, a gap or an overlap; and last, how many there are once merged.
/// </remarks>
public sealed record AnsweredStretches
{
    public const int MostStretchesAnHour = 200;

    public static readonly TimeSpan Resolution = TimeSpan.FromMilliseconds(10);

    private const double HundredthsASecond = 100;

    private AnsweredStretches(IReadOnlyList<SegmentSpan> stretches, TimeSpan length)
    {
        Stretches = stretches;
        Length = length;
    }

    /// <summary>
    /// The kinds an answer may give a stretch: the programme and a break.
    /// </summary>
    public static IReadOnlyList<SegmentKind> KindsTaken { get; } = [SegmentKind.Programme, SegmentKind.Break];

    public IReadOnlyList<SegmentSpan> Stretches { get; }

    public TimeSpan Length { get; }

    /// <summary>
    /// The length a recording written for <paramref name="written"/> is answered against, to <see cref="Resolution"/>.
    /// </summary>
    public static TimeSpan LengthOf(TimeSpan written)
        => At(Hundredths(written.TotalSeconds));

    public static AnswerReading Read(IReadOnlyList<SentStretch?> sent, TimeSpan written)
    {
        ArgumentNullException.ThrowIfNull(sent);
        ArgumentOutOfRangeException.ThrowIfLessThan(written, TimeSpan.Zero);

        double length = Hundredths(written.TotalSeconds);
        List<(double Starts, double Ends, SegmentKind Kind)> read = [];

        foreach (SentStretch? stretch in sent)
        {
            if (Placed(stretch, length) is not { } placed)
            {
                return AnswerReading.Refused(FaultOf(stretch, length));
            }

            read.Add(placed);
        }

        (double Starts, double Ends, SegmentKind Kind)[] ordered = [.. read.OrderBy(stretch => stretch.Starts).ThenBy(stretch => stretch.Ends)];

        if (Uncovered(ordered, length) is { } fault)
        {
            return AnswerReading.Refused(fault);
        }

        List<(double Starts, double Ends, SegmentKind Kind)> merged = Merged(ordered);

        return merged.Count * 3600 * HundredthsASecond > MostStretchesAnHour * length
            ? AnswerReading.Refused(AnswerFault.TooMany)
            : new AnswerReading(
                new AnsweredStretches([.. merged.Select(stretch => new SegmentSpan(At(stretch.Starts), At(stretch.Ends), stretch.Kind, null))], At(length)),
                null);
    }

    private static (double Starts, double Ends, SegmentKind Kind)? Placed(SentStretch? stretch, double length)
    {
        if (stretch is not { Kind: { } kind } || !KindsTaken.Contains(kind))
        {
            return null;
        }

        double starts = Hundredths(stretch.StartsAtSec);
        double ends = Hundredths(stretch.EndsAtSec);

        return starts < ends && starts >= 0 && ends <= length ? (starts, ends, kind) : null;
    }

    private static AnswerFault FaultOf(SentStretch? stretch, double length)
    {
        if (stretch is not { Kind: { } kind } || !KindsTaken.Contains(kind))
        {
            return AnswerFault.UnknownKind;
        }

        double starts = Hundredths(stretch.StartsAtSec);
        double ends = Hundredths(stretch.EndsAtSec);

        return starts >= ends ? AnswerFault.EndsBeforeItStarts : AnswerFault.OutsideTheRecording;
    }

    private static AnswerFault? Uncovered(IReadOnlyList<(double Starts, double Ends, SegmentKind Kind)> ordered, double length)
    {
        if (ordered.Count is 0 || ordered[0].Starts > 0)
        {
            return AnswerFault.Gap;
        }

        for (int at = 1; at < ordered.Count; at++)
        {
            if (ordered[at].Starts != ordered[at - 1].Ends)
            {
                return ordered[at].Starts > ordered[at - 1].Ends ? AnswerFault.Gap : AnswerFault.Overlap;
            }
        }

        return ordered[^1].Ends < length ? AnswerFault.Gap : null;
    }

    private static List<(double Starts, double Ends, SegmentKind Kind)> Merged(IReadOnlyList<(double Starts, double Ends, SegmentKind Kind)> ordered)
    {
        List<(double Starts, double Ends, SegmentKind Kind)> merged = [];

        foreach ((double Starts, double Ends, SegmentKind Kind) stretch in ordered)
        {
            if (merged.Count > 0 && merged[^1].Kind == stretch.Kind)
            {
                merged[^1] = (merged[^1].Starts, stretch.Ends, stretch.Kind);
            }
            else
            {
                merged.Add(stretch);
            }
        }

        return merged;
    }

    private static double Hundredths(double seconds)
        => double.IsFinite(seconds) ? Math.Round(seconds * HundredthsASecond, MidpointRounding.AwayFromZero) : double.NaN;

    private static TimeSpan At(double hundredths) => TimeSpan.FromTicks((long)hundredths * Resolution.Ticks);
}
