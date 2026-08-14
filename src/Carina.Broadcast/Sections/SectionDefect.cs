namespace Carina.Broadcast.Sections;

public enum SectionDefect
{
    PacketNotSynchronised = 1,

    TransportError = 2,

    Scrambled = 3,

    ContinuityBroken = 4,

    PointerOutOfRange = 5,

    LengthOutOfRange = 6,

    ShortFormSection = 7,

    ChecksumMismatch = 8,

    Truncated = 9,
}
