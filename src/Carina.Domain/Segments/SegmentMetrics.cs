namespace Carina.Domain.Segments;

/// <summary>
/// The seconds of one kind both answered and detected, as a share of the seconds answered as it (recall)
/// and of the seconds detected as it (precision); a share of nothing is not known.
/// </summary>
public sealed record KindMetrics(SegmentKind Kind, double? Recall, double? Precision);

/// <summary>
/// How a version detects the confirmed recordings it is judged on: the share of the answers' boundaries
/// with a detected boundary turning the same way within <see cref="NearWithin"/> and within
/// <see cref="CloseWithin"/>, not known without one; the detected boundaries with no answered boundary
/// within <see cref="NearWithin"/>, per recording; the seconds answered as programme that a button would
/// offer to skip, per hour of the recordings; the seconds of breaks found; and the share of skips on its
/// buttons that viewers came back from, and how many skips that is of. A detection thrown away shows
/// nothing, so it has no boundaries and offers no skip.
/// </summary>
public sealed record SegmentMetrics
{
    public const double MostProgrammeSkippedSecondsAnHour = 5;

    public static readonly TimeSpan NearWithin = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan CloseWithin = TimeSpan.FromSeconds(1);

    private const int ThresholdSteps = 100;

    public SegmentMetrics(
        double? boundaryRecallWithin2s,
        double? boundaryRecallWithin1s,
        double extraBoundariesPerRecording,
        double programmeSkippedSecondsPerHour,
        IReadOnlyList<KindMetrics> kinds,
        double? returnedAfterSkipShare,
        int skips)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentOutOfRangeException.ThrowIfNegative(skips);

        BoundaryRecallWithin2s = boundaryRecallWithin2s;
        BoundaryRecallWithin1s = boundaryRecallWithin1s;
        ExtraBoundariesPerRecording = extraBoundariesPerRecording;
        ProgrammeSkippedSecondsPerHour = programmeSkippedSecondsPerHour;
        Kinds = [.. kinds];
        ReturnedAfterSkipShare = returnedAfterSkipShare;
        Skips = skips;
    }

    public double? BoundaryRecallWithin2s { get; }

    public double? BoundaryRecallWithin1s { get; }

    public double ExtraBoundariesPerRecording { get; }

    public double ProgrammeSkippedSecondsPerHour { get; }

    public IReadOnlyList<KindMetrics> Kinds { get; }

    public double? ReturnedAfterSkipShare { get; }

    public int Skips { get; }

    /// <summary>
    /// These figures beside what viewers did with the version's buttons as <paramref name="returns"/> counts.
    /// </summary>
    public SegmentMetrics WithReturns(SkipReturns returns)
    {
        ArgumentNullException.ThrowIfNull(returns);

        return new SegmentMetrics(
            BoundaryRecallWithin2s,
            BoundaryRecallWithin1s,
            ExtraBoundariesPerRecording,
            ProgrammeSkippedSecondsPerHour,
            Kinds,
            returns.Share,
            returns.Skips);
    }

    /// <summary>
    /// The figures of <paramref name="compared"/> with buttons offered from <paramref name="skipThreshold"/>,
    /// beside what viewers did with the version's buttons; none for no recording.
    /// </summary>
    public static SegmentMetrics? Of(IReadOnlyList<SegmentComparison> compared, double skipThreshold, SkipReturns returns)
    {
        ArgumentNullException.ThrowIfNull(compared);
        ArgumentNullException.ThrowIfNull(returns);

        if (compared.Count is 0)
        {
            return null;
        }

        Boundary[][] answered = [.. compared.Select(comparison => Boundaries(comparison.Answer))];
        Boundary[][] detected = [.. compared.Select(comparison => Boundaries(comparison.Shown))];
        int extra = detected.Select((found, at) => found.Count(boundary => !answered[at].Any(answer => Near(answer.At, boundary.At, NearWithin)))).Sum();

        return new SegmentMetrics(
            Recall(answered, detected, NearWithin),
            Recall(answered, detected, CloseWithin),
            extra / (double)compared.Count,
            ProgrammeSkippedAnHour(compared, skipThreshold),
            [Seconds(compared, SegmentKind.Break)],
            returns.Share,
            returns.Skips);
    }

    /// <summary>
    /// The seconds answered as programme in <paramref name="compared"/> that a button offered from
    /// <paramref name="skipThreshold"/> would skip, per hour of the recordings.
    /// </summary>
    public static double ProgrammeSkippedAnHour(IReadOnlyList<SegmentComparison> compared, double skipThreshold)
    {
        ArgumentNullException.ThrowIfNull(compared);

        double seconds = compared.Sum(comparison => Overlap(
            Of(comparison.Answer, SegmentKind.Programme),
            [.. PlaybackChapter.Of(comparison.Shown, PlaybackFileClock.AsItIs, skipThreshold).Where(chapter => chapter.Skippable).Select(chapter => chapter.Stretch)]));
        double hours = compared.Sum(comparison => comparison.Length.TotalHours);

        return hours > 0 ? seconds / hours : 0;
    }

    /// <summary>
    /// The lowest threshold a skip may be set to, in hundredths from
    /// <see cref="SegmentSettingsStanding.LowestSkipThreshold"/> to
    /// <see cref="SegmentSettingsStanding.HighestSkipThreshold"/>, at which no more than
    /// <see cref="MostProgrammeSkippedSecondsAnHour"/> seconds of programme an hour would be skippable;
    /// none when no recording is judged or no threshold holds.
    /// </summary>
    public static double? RecommendedThreshold(IReadOnlyList<SegmentComparison> compared)
    {
        ArgumentNullException.ThrowIfNull(compared);

        if (compared.Count is 0)
        {
            return null;
        }

        int lowest = (int)Math.Round(SegmentSettingsStanding.LowestSkipThreshold * ThresholdSteps);
        int highest = (int)Math.Round(SegmentSettingsStanding.HighestSkipThreshold * ThresholdSteps);

        for (int step = lowest; step <= highest; step++)
        {
            double threshold = step / (double)ThresholdSteps;

            if (ProgrammeSkippedAnHour(compared, threshold) <= MostProgrammeSkippedSecondsAnHour)
            {
                return threshold;
            }
        }

        return null;
    }

    /// <summary>
    /// Where the kind turns in stretches laid end to end, and which way.
    /// </summary>
    internal static Boundary[] Boundaries(IReadOnlyList<SegmentSpan> spans)
        => [.. spans.Zip(spans.Skip(1)).Where(pair => pair.First.Kind != pair.Second.Kind).Select(pair => new Boundary(pair.Second.Starts, pair.First.Kind, pair.Second.Kind))];

    private static double? Recall(Boundary[][] answered, Boundary[][] detected, TimeSpan within)
    {
        int total = answered.Sum(boundaries => boundaries.Length);
        int found = answered.Select((boundaries, at) => boundaries.Count(answer => detected[at].Any(boundary => answer.Turns(boundary) && Near(answer.At, boundary.At, within)))).Sum();

        return total is 0 ? null : found / (double)total;
    }

    private static KindMetrics Seconds(IReadOnlyList<SegmentComparison> compared, SegmentKind kind)
    {
        double answered = compared.Sum(comparison => Of(comparison.Answer, kind).Sum(span => span.Length.TotalSeconds));
        double detected = compared.Sum(comparison => Of(comparison.Shown, kind).Sum(span => span.Length.TotalSeconds));
        double both = compared.Sum(comparison => Overlap(Of(comparison.Answer, kind), Of(comparison.Shown, kind)));

        return new KindMetrics(kind, answered > 0 ? both / answered : null, detected > 0 ? both / detected : null);
    }

    private static SegmentSpan[] Of(IReadOnlyList<SegmentSpan> spans, SegmentKind kind) => [.. spans.Where(span => span.Kind == kind)];

    private static double Overlap(SegmentSpan[] one, SegmentSpan[] other)
        => one.Sum(span => other.Sum(against => Math.Max(0, ((span.Ends < against.Ends ? span.Ends : against.Ends) - (span.Starts > against.Starts ? span.Starts : against.Starts)).TotalSeconds)));

    private static bool Near(TimeSpan one, TimeSpan other, TimeSpan within) => (one - other).Duration() <= within;

    internal readonly record struct Boundary(TimeSpan At, SegmentKind From, SegmentKind To)
    {
        public bool Turns(Boundary other) => From == other.From && To == other.To;
    }
}
