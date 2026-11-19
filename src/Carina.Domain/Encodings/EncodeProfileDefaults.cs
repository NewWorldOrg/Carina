using Carina.Domain.Machines;

namespace Carina.Domain.Encodings;

public sealed record EncodeProfileDefaults
{
    public const int RateFactor = 20;

    public const int Quantiser = 26;

    private EncodeProfileDefaults(EncodeCodec codec)
    {
        Codec = codec;
    }

    public EncodeCodec Codec { get; }

    public EncodeResolution Resolution => EncodeResolution.AsSource;

    public Deinterlace Deinterlace => Deinterlace.EveryField;

    public ConstantRateFactor SoftwareRateControl => new(RateFactor);

    public ConstantQuantiser VaapiRateControl => new(Quantiser);

    public static EncodeProfileDefaults For(MachineCapabilities can)
    {
        ArgumentNullException.ThrowIfNull(can);

        return new EncodeProfileDefaults(
            can.Has(Faculty.EncodeH265OnTheCard) || can.Has(Faculty.EncodeH265OnTheProcessor)
                ? EncodeCodec.H265
                : EncodeCodec.H264);
    }
}
