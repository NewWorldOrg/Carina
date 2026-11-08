using Carina.Domain.Machines;

namespace Carina.Domain.Encodings;

public static class EncodeEncoders
{
    public static bool OnTheCard(EncodeEncoder encoder) => EncodeShapes.Named(encoder) is not EncodeEncoder.Software;

    /// <summary>
    /// The card an encoder runs on, or none for the processor.
    /// </summary>
    public static CardKind? CardOf(EncodeEncoder encoder)
        => EncodeShapes.Named(encoder) switch
        {
            EncodeEncoder.Vaapi => CardKind.Vaapi,
            EncodeEncoder.Nvenc => CardKind.Nvenc,
            _ => null,
        };

    /// <summary>
    /// The encoder that runs on <paramref name="card"/>.
    /// </summary>
    public static EncodeEncoder On(CardKind card)
        => CardKinds.Named(card) is CardKind.Nvenc ? EncodeEncoder.Nvenc : EncodeEncoder.Vaapi;
}
