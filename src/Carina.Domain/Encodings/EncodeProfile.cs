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

    public DateTime? RetiredAt { get; private set; }

    public bool IsRetired => RetiredAt is not null;

    public static EncodeProfile Define(
        EncodeProfileId id,
        EncodeLabel label,
        EncodeCodec codec,
        EncodeResolution resolution,
        Deinterlace deinterlace,
        ConstantRateFactor softwareRateControl,
        ConstantQuantiser vaapiRateControl,
        DateTime at)
        => Rehydrate(id, label, codec, resolution, deinterlace, softwareRateControl, vaapiRateControl, at, null);

    public static EncodeProfile Rehydrate(
        EncodeProfileId id,
        EncodeLabel label,
        EncodeCodec codec,
        EncodeResolution resolution,
        Deinterlace deinterlace,
        ConstantRateFactor softwareRateControl,
        ConstantQuantiser vaapiRateControl,
        DateTime definedAt,
        DateTime? retiredAt)
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
            RetiredAt = UtcTimes.Optional(retiredAt, nameof(retiredAt)),
        };
    }

    public void Revise(
        EncodeLabel label,
        EncodeCodec codec,
        EncodeResolution resolution,
        Deinterlace deinterlace,
        ConstantRateFactor softwareRateControl,
        ConstantQuantiser vaapiRateControl)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(softwareRateControl);
        ArgumentNullException.ThrowIfNull(vaapiRateControl);
        RefuseWhileRetired();

        EncodeCodec named = EncodeShapes.Named(codec);
        EncodeResolution sized = EncodeShapes.Named(resolution);
        Deinterlace undone = EncodeShapes.Named(deinterlace);

        Label = label;
        Codec = named;
        Resolution = sized;
        Deinterlace = undone;
        SoftwareRateControl = softwareRateControl;
        VaapiRateControl = vaapiRateControl;
    }

    public void Retire(DateTime at)
    {
        RefuseWhileRetired();

        RetiredAt = UtcTimes.Required(at, nameof(at));
    }

    private void RefuseWhileRetired()
    {
        if (IsRetired)
        {
            throw new InvalidOperationException($"Profile {Id.Wire} was retired at {RetiredAt:O} and is not moved again.");
        }
    }
}
