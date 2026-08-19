namespace Carina.BroadcastTestSupport;

public sealed class EitWriter
{
    private static readonly TimeSpan BroadcastOffset = TimeSpan.FromHours(9);

    public required int TransportStreamId { get; init; }

    public required int OriginalNetworkId { get; init; }

    public int SegmentLastSectionNumber { get; init; }

    public required int LastTableId { get; init; }

    public byte[][] Events { get; init; } = [];

    public static byte[] Event(
        int eventId,
        DateTimeOffset startsAt,
        TimeSpan? runs,
        byte[] descriptors,
        int runningStatus = 1,
        bool isScrambled = false)
        => new ByteWriter()
            .Word(eventId)
            .Run(Start(startsAt))
            .Run(Duration(runs))
            .Byte((runningStatus << 5) | (isScrambled ? 0x10 : 0) | ((descriptors.Length >> 8) & 0x0F))
            .Byte(descriptors.Length & 0xFF)
            .Run(descriptors)
            .ToArray();

    public static byte[] Start(DateTimeOffset at)
    {
        DateTimeOffset broadcast = at.ToOffset(BroadcastOffset);
        int days = ModifiedJulianDay(broadcast.Year, broadcast.Month, broadcast.Day);

        return
        [
            (byte)(days >> 8),
            (byte)(days & 0xFF),
            Packed(broadcast.Hour),
            Packed(broadcast.Minute),
            Packed(broadcast.Second),
        ];
    }

    public static byte[] Duration(TimeSpan? runs)
        => runs is { } held
            ? [Packed((int)held.TotalHours), Packed(held.Minutes), Packed(held.Seconds)]
            : [0xFF, 0xFF, 0xFF];

    public byte[] ToBody()
        => new ByteWriter()
            .Word(TransportStreamId)
            .Word(OriginalNetworkId)
            .Byte(SegmentLastSectionNumber)
            .Byte(LastTableId)
            .Run(Events.SelectMany(held => held).ToArray())
            .ToArray();

    private static int ModifiedJulianDay(int year, int month, int day)
    {
        int shiftedYear = month <= 2 ? year - 1901 : year - 1900;
        int shiftedMonth = month <= 2 ? month + 13 : month + 1;

        return 14956 + day + (int)(shiftedYear * 365.25) + (int)(shiftedMonth * 30.6001);
    }

    private static byte Packed(int value) => (byte)(((value / 10) << 4) | (value % 10));
}
