using Carina.Domain.Machines;

namespace Carina.Domain.Encodings;

/// <summary>
/// What a new profile starts from on this machine: the source's own size, every field undone into a picture of
/// its own, and the same rate factor and quantiser whichever the codec. The codec is H.265 when this machine
/// writes it on the card or on the processor, and H.264 otherwise.
/// </summary>
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
