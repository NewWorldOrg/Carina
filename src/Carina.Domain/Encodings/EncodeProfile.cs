using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

public sealed class EncodeProfile
{
    private EncodeProfile()
    {
    }

    public EncodeProfileId Id { get; private set; } = null!;

    public EncodeLabel Label { get; private set; } = null!;

    public EncodeCodec Codec { get; private set; }

    public EncodeResolution Resolution { get; private set; }

    public Deinterlace Deinterlace { get; private set; }

    public ConstantRateFactor SoftwareRateControl { get; private set; } = null!;

    public ConstantQuantiser VaapiRateControl { get; private set; } = null!;

    public DateTime DefinedAt { get; private set; }

    public static EncodeProfile Define(
        EncodeProfileId id,
        EncodeLabel label,
        EncodeCodec codec,
        EncodeResolution resolution,
        Deinterlace deinterlace,
        ConstantRateFactor softwareRateControl,
        ConstantQuantiser vaapiRateControl,
        DateTime at)
        => Rehydrate(id, label, codec, resolution, deinterlace, softwareRateControl, vaapiRateControl, at);

    public static EncodeProfile Rehydrate(
        EncodeProfileId id,
        EncodeLabel label,
        EncodeCodec codec,
        EncodeResolution resolution,
        Deinterlace deinterlace,
        ConstantRateFactor softwareRateControl,
        ConstantQuantiser vaapiRateControl,
        DateTime definedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(softwareRateControl);
        ArgumentNullException.ThrowIfNull(vaapiRateControl);

        return new EncodeProfile
        {
            Id = id,
            Label = label,
            Codec = EncodeShapes.Named(codec),
            Resolution = EncodeShapes.Named(resolution),
            Deinterlace = EncodeShapes.Named(deinterlace),
            SoftwareRateControl = softwareRateControl,
            VaapiRateControl = vaapiRateControl,
            DefinedAt = UtcTimes.Required(definedAt, nameof(definedAt)),
        };
    }
}
