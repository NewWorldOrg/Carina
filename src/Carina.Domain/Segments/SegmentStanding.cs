namespace Carina.Domain.Segments;

public enum SegmentSource
{
    None = 1,

    Detected = 2,
}

public enum SegmentStandingState
{
    None = 1,

    Marked = 2,

    NothingFound = 3,

    Discarded = 4,
}

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

    public bool Marks => Played.Any(stretch => stretch.Kind is not SegmentKind.Programme);

    public double BreakShare => Detection?.CmShare ?? 0;

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
