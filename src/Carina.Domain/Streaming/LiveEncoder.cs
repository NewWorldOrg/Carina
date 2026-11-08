using Carina.Domain.Machines;

namespace Carina.Domain.Streaming;

public enum LiveEncoder
{
    Software = 1,

    Vaapi = 2,

    Nvenc = 3,
}

public static class LiveEncoders
{
    public static LiveEncoder Named(LiveEncoder encoder)
        => Enum.IsDefined(encoder)
            ? encoder
            : throw new ArgumentOutOfRangeException(
                nameof(encoder),
                encoder,
                "A picture is encoded by the processor or by one of the cards named here.");

    public static bool OnTheCard(LiveEncoder encoder) => Named(encoder) is not LiveEncoder.Software;

    /// <summary>
    /// The card an encoder runs on, or none for the processor.
    /// </summary>
    public static CardKind? CardOf(LiveEncoder encoder)
        => Named(encoder) switch
        {
            LiveEncoder.Vaapi => CardKind.Vaapi,
            LiveEncoder.Nvenc => CardKind.Nvenc,
            _ => null,
        };
}
