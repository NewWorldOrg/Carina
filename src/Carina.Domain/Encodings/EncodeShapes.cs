namespace Carina.Domain.Encodings;

public static class EncodeShapes
{
    public static EncodeCodec Named(EncodeCodec codec)
        => Enum.IsDefined(codec)
            ? codec
            : throw new ArgumentOutOfRangeException(nameof(codec), codec, "A codec is one of the two on offer.");

    public static EncodeResolution Named(EncodeResolution resolution)
        => Enum.IsDefined(resolution)
            ? resolution
            : throw new ArgumentOutOfRangeException(
                nameof(resolution),
                resolution,
                "A resolution is the source's own or one of the named sizes.");

    public static Deinterlace Named(Deinterlace deinterlace)
        => Enum.IsDefined(deinterlace)
            ? deinterlace
            : throw new ArgumentOutOfRangeException(
                nameof(deinterlace),
                deinterlace,
                "Interlacing is left alone or undone one of the two ways.");

    public static EncodeEncoder Named(EncodeEncoder encoder)
        => Enum.IsDefined(encoder)
            ? encoder
            : throw new ArgumentOutOfRangeException(
                nameof(encoder),
                encoder,
                "A picture is encoded either by the processor or by the card.");
}
