namespace Carina.Domain.Segments;

/// <summary>
/// Where the stretches a recording is played with come from.
/// </summary>
public enum SegmentSource
{
    None = 1,

    Detected = 2,
}

/// <summary>
/// How the stretches a recording is played with stand: none, breaks marked, only the programme found, or the
/// sequence thrown away.
/// </summary>
public enum SegmentStandingState
{
    None = 1,

    Marked = 2,

    NothingFound = 3,

    Discarded = 4,
}

/// <summary>
/// The one place that decides which stretches a recording is played with: those the version of the model in use
/// detected, none when its sequence was thrown away, and none when it has not detected the recording.
/// </summary>
public sealed record SegmentStanding
{
    private SegmentStanding(SegmentSource source, SegmentStandingState state, RecordingDetection? detection)
    {
        Source = source;
        State = state;
        Detection = detection;
    }

    public static SegmentStanding None { get; } = new(SegmentSource.None, SegmentStandingState.None, null);

    public SegmentSource Source { get; }

    public SegmentStandingState State { get; }

    public RecordingDetection? Detection { get; }

    public IReadOnlyList<SegmentSpan> Played
        => Detection is { State: not SegmentDetectionState.Discarded } detection ? detection.Segments : [];

    /// <summary>
    /// Whether anything but the programme is played.
    /// </summary>
    public bool Marks => Played.Any(stretch => stretch.Kind is not SegmentKind.Programme);

    /// <summary>
    /// The share of the length the detection gives to breaks, whatever its state.
    /// </summary>
    public double BreakShare => Detection?.CmShare ?? 0;

    /// <summary>
    /// The version of the model that detected the stretches played.
    /// </summary>
    public int? ModelVersion => Detection?.ModelVersion;

    public static SegmentStanding Of(RecordingDetection? detection)
        => detection is null ? None : new SegmentStanding(SegmentSource.Detected, StateOf(detection.State), detection);

    private static SegmentStandingState StateOf(SegmentDetectionState state) => state switch
    {
        SegmentDetectionState.Marked => SegmentStandingState.Marked,
        SegmentDetectionState.NothingFound => SegmentStandingState.NothingFound,
        _ => SegmentStandingState.Discarded,
    };
}
