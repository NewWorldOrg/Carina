namespace Carina.Domain.Streaming;

public enum VideoCodec
{
    H264 = 1,

    H265 = 2,

    Av1 = 3,
}

public static class VideoCodecs
{
    public const string H264IsCalled = "h264";

    public const string H265IsCalled = "h265";

    public const string Av1IsCalled = "av1";

    public const VideoCodec Unasked = VideoCodec.H264;

    public static readonly IReadOnlyList<VideoCodec> InOrder = [VideoCodec.H264, VideoCodec.H265, VideoCodec.Av1];

    public static readonly IReadOnlyList<string> Names = [H264IsCalled, H265IsCalled, Av1IsCalled];

    public static string NameOf(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => H264IsCalled,
        VideoCodec.H265 => H265IsCalled,
        VideoCodec.Av1 => Av1IsCalled,
        _ => throw Unknown(codec),
    };

    public static VideoCodec? Find(string? name)
        => InOrder.Cast<VideoCodec?>().FirstOrDefault(codec =>
            string.Equals(NameOf(codec!.Value), name, StringComparison.Ordinal));

    public static VideoCodec Named(VideoCodec codec) => Enum.IsDefined(codec) ? codec : throw Unknown(codec);

    private static ArgumentOutOfRangeException Unknown(VideoCodec codec)
        => new(nameof(codec), codec, "A picture is encoded in one of the formats named here.");
}
