namespace Carina.Broadcast.DsmCc;

public sealed record BxmlInfo(
    int TransmissionFormat,
    bool IsEntryPoint,
    bool AutoStart,
    int DocumentResolution,
    int BmlMajorVersion,
    int BmlMinorVersion,
    int? DataEventId)
{
    public const int DataCarouselFormat = 0;

    private const int EntryFieldsSize = 6;

    private const int XmlVersionSize = 4;

    private const int CarouselInfoSize = 2;

    public static BxmlInfo? Read(ReadOnlySpan<byte> info)
    {
        if (info.IsEmpty)
        {
            return null;
        }

        int format = info[0] >> 6;
        bool entry = (info[0] & 0x20) != 0;

        if (!entry)
        {
            return WithCarouselInfo(new BxmlInfo(format, false, false, 0, 0, 0, null), info[1..]);
        }

        if (info.Length < EntryFieldsSize)
        {
            return null;
        }

        bool usesXml = (info[1] & 0x80) != 0;
        int carouselInfoAt = EntryFieldsSize + (usesXml ? XmlVersionSize : 0);

        if (info.Length < carouselInfoAt)
        {
            return null;
        }

        BxmlInfo read = new(
            format,
            true,
            (info[0] & 0x10) != 0,
            info[0] & 0x0F,
            (info[2] << 8) | info[3],
            (info[4] << 8) | info[5],
            null);

        return WithCarouselInfo(read, info[carouselInfoAt..]);
    }

    private static BxmlInfo? WithCarouselInfo(BxmlInfo read, ReadOnlySpan<byte> rest)
    {
        if (read.TransmissionFormat != DataCarouselFormat)
        {
            return read;
        }

        return rest.Length < CarouselInfoSize ? null : read with { DataEventId = rest[0] >> 4 };
    }
}
