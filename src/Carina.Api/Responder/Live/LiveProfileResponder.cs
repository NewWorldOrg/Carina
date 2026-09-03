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
    bool Unasked)
{
    public static LiveProfileResponder Of(LiveProfile profile, LiveProfile unasked)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(unasked);

        return new LiveProfileResponder(
            profile.Name,
            profile.Codec,
            profile.Size.Width,
            profile.Size.Height,
            new LiveFrameRateResponder(profile.Rate.Numerator, profile.Rate.Denominator),
            profile.SoftwareRateControl.KilobitsPerSecond,
            profile.VaapiRateControl.Quantiser,
            ReferenceEquals(profile, unasked));
    }
}
