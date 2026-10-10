namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// What a resource of a module is, as the side channel and the recording's module answer tell it in one byte.
/// </summary>
public enum DataBroadcastResourceKind : byte
{
    Bml = 1,

    Css = 2,

    EcmaScript = 3,

    Jpeg = 4,

    Png = 5,

    OtherBinary = 6,

    UndecodedText = 7,
}
