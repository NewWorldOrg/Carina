using Carina.Domain.Encodings;

namespace Carina.Api.Responder.Encoding;

public sealed record EncodeProfileResponder(
    Guid Id,
    string Label,
    EncodeCodec Codec,
    EncodeResolution Resolution,
    Deinterlace Deinterlace,
    int RateFactor,
    int Quantiser,
    DateTime DefinedAt)
{
    public static EncodeProfileResponder Of(EncodeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new EncodeProfileResponder(
            profile.Id.Value,
            profile.Label.Value,
            profile.Codec,
            profile.Resolution,
            profile.Deinterlace,
            profile.SoftwareRateControl.RateFactor,
            profile.VaapiRateControl.Quantiser,
            profile.DefinedAt);
    }
}

public sealed record EncodeProfileListResponder(IReadOnlyList<EncodeProfileResponder> Items)
{
    public static EncodeProfileListResponder Of(IReadOnlyList<EncodeProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        return new EncodeProfileListResponder([.. profiles.Select(EncodeProfileResponder.Of)]);
    }
}
