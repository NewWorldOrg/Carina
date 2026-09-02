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
}
