using Carina.Domain.Streaming;

namespace Carina.Api.Responder.Live;

public sealed record LiveFrameRateResponder(int Numerator, int Denominator);

public sealed record LiveProfileResponder(
    string Name,
    VideoCodec Codec,
    int Width,
    int Height,
    LiveFrameRateResponder FrameRate,
    int SoftwareKilobitsPerSecond,
    int VaapiQuantiser,
    bool Unasked,
    IReadOnlyList<VideoCodec> Formats)
{
    /// <summary>
    /// A profile, named with the format it is encoded in when none is asked for and every format this machine
    /// transcodes it in.
    /// </summary>
    public static LiveProfileResponder Of(LiveProfile profile, LiveProfile unasked, IReadOnlyList<VideoCodec> formats)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(unasked);
        ArgumentNullException.ThrowIfNull(formats);

        return new LiveProfileResponder(
            profile.Name,
            VideoCodecs.Unasked,
            profile.Size.Width,
            profile.Size.Height,
            new LiveFrameRateResponder(profile.Rate.Numerator, profile.Rate.Denominator),
            profile.SoftwareRateControl.KilobitsPerSecond,
            profile.VaapiRateControl.H264.Quantiser,
            ReferenceEquals(profile, unasked),
            formats);
    }
}
