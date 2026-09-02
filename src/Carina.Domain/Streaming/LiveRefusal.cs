namespace Carina.Domain.Streaming;

public enum LiveRefusal
{
    NoSuchChannel = 1,

    NoTunerFree = 2,

    WouldNotTune = 3,

    DriverUnavailable = 4,

    TooManyAlready = 5,

    TranscoderWouldNotStart = 6,
}

public static class LiveRefusals
{
    public static IReadOnlyList<LiveRefusal> FromTheSupply { get; } =
    [
        LiveRefusal.NoSuchChannel,
        LiveRefusal.NoTunerFree,
        LiveRefusal.WouldNotTune,
        LiveRefusal.DriverUnavailable,
    ];

    public static IReadOnlyList<LiveRefusal> FromTheTranscoder { get; } =
    [
        LiveRefusal.TooManyAlready,
        LiveRefusal.TranscoderWouldNotStart,
    ];
}
