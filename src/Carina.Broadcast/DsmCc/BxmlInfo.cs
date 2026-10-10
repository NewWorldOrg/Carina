namespace Carina.Broadcast.DsmCc;

public sealed record BxmlInfo
{
    public const int DataCarouselFormat = 0;

    public const int ReservedFormat = 1;

    private const int VersionPairSize = 4;

    private const int CarouselInfoSize = 2;

    public int TransmissionFormat { get; private init; }

    public bool EntryPointFlag { get; private init; }

    public bool AutoStart { get; private init; }

    public int DocumentResolution { get; private init; }

    public bool UsesXml { get; private init; }

    public bool UsesDefaultVersion { get; private init; }

    public bool IsIndependent { get; private init; }

    public bool StyleForTv { get; private init; }

    public int? BmlMajorVersion { get; private init; }

    public int? BmlMinorVersion { get; private init; }

    public int? BxmlMajorVersion { get; private init; }

    public int? BxmlMinorVersion { get; private init; }

    public int? DataEventId { get; private init; }

    public static BxmlInfo? Read(ReadOnlySpan<byte> info)
    {
        if (info.IsEmpty)
        {
            return null;
        }

        BxmlInfo read = new()
        {
            TransmissionFormat = info[0] >> 6,
            EntryPointFlag = (info[0] & 0x20) != 0,
        };
        int at = 1;

        if (read.EntryPointFlag && !TryReadEntry(info, ref at, ref read))
        {
            return null;
        }

        return WithCarouselInfo(read, info[at..]);
    }

    private static bool TryReadEntry(ReadOnlySpan<byte> info, ref int at, ref BxmlInfo read)
    {
        if (info.Length < 2)
        {
            return false;
        }

        read = read with
        {
            AutoStart = (info[0] & 0x10) != 0,
            DocumentResolution = info[0] & 0x0F,
            UsesXml = (info[1] & 0x80) != 0,
            UsesDefaultVersion = (info[1] & 0x40) != 0,
            IsIndependent = (info[1] & 0x20) != 0,
            StyleForTv = (info[1] & 0x10) != 0,
        };
        at = 2;

        if (read.UsesDefaultVersion)
        {
            return true;
        }

        int versionsSize = read.UsesXml ? 2 * VersionPairSize : VersionPairSize;

        if (info.Length < at + versionsSize)
        {
            return false;
        }

        read = read with
        {
            BmlMajorVersion = Word(info, at),
            BmlMinorVersion = Word(info, at + 2),
            BxmlMajorVersion = read.UsesXml ? Word(info, at + 4) : null,
            BxmlMinorVersion = read.UsesXml ? Word(info, at + 6) : null,
        };
        at += versionsSize;

        return true;
    }

    private static BxmlInfo? WithCarouselInfo(BxmlInfo read, ReadOnlySpan<byte> rest)
        => read.TransmissionFormat switch
        {
            DataCarouselFormat when rest.Length >= CarouselInfoSize => read with { DataEventId = rest[0] >> 4 },
            DataCarouselFormat => null,
            ReservedFormat when rest.IsEmpty => null,
            _ => read,
        };

    private static int Word(ReadOnlySpan<byte> info, int at) => (info[at] << 8) | info[at + 1];
}
