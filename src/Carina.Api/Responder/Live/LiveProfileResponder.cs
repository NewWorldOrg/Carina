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
    int VaapiQuantiser)
{
    public static LiveProfileResponder Of(LiveProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new LiveProfileResponder(
            profile.Name,
            profile.Codec,
            profile.Size.Width,
            profile.Size.Height,
            new LiveFrameRateResponder(profile.Rate.Numerator, profile.Rate.Denominator),
            profile.SoftwareRateControl.KilobitsPerSecond,
            profile.VaapiRateControl.Quantiser);
    }
}
