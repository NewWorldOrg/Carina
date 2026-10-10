namespace Carina.BroadcastTestSupport;

public sealed class StreamDescriptorWriter
{
    public const int NptReferenceTag = 0x17;

    public const int GeneralEventTag = 0x40;

    public const int Immediate = 0x00;

    public const int MjdJst = 0x01;

    public const int Npt = 0x02;

    public int DataEventId { get; init; }

    public int EventMessageGroupId { get; init; }

    public int VersionNumber { get; init; }

    public bool IsCurrent { get; init; } = true;

    public byte[] Descriptors { get; init; } = [];

    public SectionWriter ToSection()
        => new()
        {
            TableId = DsmCcWriter.StreamDescriptorsTableId,
            TableIdExtension = ((DataEventId & 0x0F) << 12) | (EventMessageGroupId & 0x0FFF),
            VersionNumber = VersionNumber,
            IsCurrent = IsCurrent,
            Body = Descriptors,
        };

    public static byte[] GeneralEvent(int groupId, int timeMode, long time, int messageType, int messageId, params byte[] privateData)
    {
        long carried = timeMode switch
        {
            Immediate => 0xFF_FFFF_FFFF,
            Npt => (0x7FL << 33) | (time & 0x1_FFFF_FFFF),
            _ => time,
        };

        return DescriptorWriter.Of(
            GeneralEventTag,
            new ByteWriter()
                .Word(((groupId & 0x0FFF) << 4) | 0x0F)
                .Byte(timeMode)
                .Run(BigEndian(carried, 5))
                .Byte(messageType)
                .Word(messageId)
                .Run(privateData)
                .ToArray());
    }

    public static byte[] NptReference(long stc, long npt, int scaleNumerator = 1, int scaleDenominator = 1, int contentId = 0)
        => DescriptorWriter.Of(
            NptReferenceTag,
            new ByteWriter()
                .Byte(contentId & 0x7F)
                .Run(BigEndian((0x7FL << 33) | (stc & 0x1_FFFF_FFFF), 5))
                .Run(BigEndian((0x7FFF_FFFFL << 33) | (npt & 0x1_FFFF_FFFF), 8))
                .Word(scaleNumerator)
                .Word(scaleDenominator)
                .ToArray());

    private static byte[] BigEndian(long value, int size)
    {
        byte[] bytes = new byte[size];

        for (int at = 0; at < size; at++)
        {
            bytes[at] = (byte)(value >> (8 * (size - 1 - at)));
        }

        return bytes;
    }
}
