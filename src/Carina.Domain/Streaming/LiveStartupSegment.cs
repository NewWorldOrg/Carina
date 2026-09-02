namespace Carina.Domain.Streaming;

public enum LiveStartupSegment
{
    TunerSecured = 1,

    ChannelLocked = 2,

    TranscoderStarted = 3,

    InitReached = 4,

    FirstPicture = 5,
}

public static class LiveStartupSegments
{
    public static IReadOnlyList<LiveStartupSegment> InOrder { get; } =
    [
        LiveStartupSegment.TunerSecured,
        LiveStartupSegment.ChannelLocked,
        LiveStartupSegment.TranscoderStarted,
        LiveStartupSegment.InitReached,
        LiveStartupSegment.FirstPicture,
    ];

    public static LiveStartupSegment Last => LiveStartupSegment.FirstPicture;

    public static IReadOnlyList<LiveStartupSegment> Behind(LiveStartupSegment segment)
        => segment switch
        {
            LiveStartupSegment.TunerSecured => [],
            LiveStartupSegment.ChannelLocked => [LiveStartupSegment.TunerSecured],
            LiveStartupSegment.TranscoderStarted => [LiveStartupSegment.TunerSecured],
            LiveStartupSegment.InitReached => [LiveStartupSegment.ChannelLocked, LiveStartupSegment.TranscoderStarted],
            LiveStartupSegment.FirstPicture => [LiveStartupSegment.InitReached],
            _ => throw new ArgumentOutOfRangeException(
                nameof(segment),
                segment,
                "The startup runs through one of the segments named here."),
        };
}
