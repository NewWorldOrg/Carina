namespace Carina.Domain.Segments;

/// <summary>
/// Where the stretches a recording is played with come from.
/// </summary>
public enum SegmentSource
{
    None = 1,

    Detected = 2,

    Answered = 3,
}

/// <summary>
/// How the stretches a recording is played with stand: none, breaks marked, only the programme found, the
/// sequence thrown away, or given by an answer.
/// </summary>
public enum SegmentStandingState
{
    None = 1,

    Marked = 2,

    NothingFound = 3,

    Discarded = 4,

    Answered = 5,
}

/// <summary>
/// The one place that decides which stretches a recording is played with: those of the answer a person gave,
/// while it stands, whatever was detected; otherwise those the version of the model in use detected, none when
/// its sequence was thrown away, and none when it has not detected the recording. The detection is kept beside
/// an answer for the boundary candidates it found.
/// </summary>
public sealed record SegmentStanding
{
    private SegmentStanding(SegmentSource source, SegmentStandingState state, RecordingAnswer? answer, RecordingDetection? detection)
    {
        Source = source;
        State = state;
        Answer = answer;
        Detection = detection;
    }

    public static SegmentStanding None { get; } = new(SegmentSource.None, SegmentStandingState.None, null, null);

    public SegmentSource Source { get; }

    public SegmentStandingState State { get; }

    public RecordingAnswer? Answer { get; }

    public RecordingDetection? Detection { get; }

    public IReadOnlyList<SegmentSpan> Played => Answer?.Segments ?? Detected;

    /// <summary>
    /// Whether anything but the programme is played.
    /// </summary>
    public bool Marks => Played.Any(stretch => stretch.Kind is not SegmentKind.Programme);

    /// <summary>
    /// The share of the length the stretches give to breaks: the answer's, or the detection's whatever its state.
    /// </summary>
    public double BreakShare => Answer?.BreakShare ?? Detection?.CmShare ?? 0;

    /// <summary>
    /// The version of the model that detected the stretches played; none when they are an answer's.
    /// </summary>
    public int? ModelVersion => Answer is null ? Detection?.ModelVersion : null;

    private IReadOnlyList<SegmentSpan> Detected
        => Detection is { State: not SegmentDetectionState.Discarded } detection ? detection.Segments : [];

    /// <summary>
    /// How a recording stands with <paramref name="answer"/> kept for it, standing or withdrawn, and
    /// <paramref name="detection"/> by the version in use.
    /// </summary>
    public static SegmentStanding Of(RecordingAnswer? answer, RecordingDetection? detection)
    {
        if (answer is not null && detection is not null && !answer.RecordingId.Equals(detection.RecordingId))
        {
            throw new ArgumentException("A recording stands with its own answer and its own detection.", nameof(detection));
        }

        if (answer is { Stands: true })
        {
            return new SegmentStanding(SegmentSource.Answered, SegmentStandingState.Answered, answer, detection);
        }

        return detection is null ? None : new SegmentStanding(SegmentSource.Detected, StateOf(detection.State), null, detection);
    }

    private static SegmentStandingState StateOf(SegmentDetectionState state) => state switch
    {
        SegmentDetectionState.Marked => SegmentStandingState.Marked,
        SegmentDetectionState.NothingFound => SegmentStandingState.NothingFound,
        _ => SegmentStandingState.Discarded,
    };
}
