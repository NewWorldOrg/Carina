namespace Carina.BroadcastTestSupport;

public sealed class PmtWriter
{
    public const int TableId = 0x02;

    public const int NoPcr = 0x1FFF;

    public const int PrivateData = 0x06;

    public required int ProgramNumber { get; init; }

    public int PcrPid { get; init; } = NoPcr;

    public byte[][] Streams { get; init; } = [];

    public static byte[] Stream(int streamType, int pid, byte[] descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        return new ByteWriter()
            .Byte(streamType)
            .Word(0xE000 | pid)
            .Word(0xF000 | descriptors.Length)
            .Run(descriptors)
            .ToArray();
    }

    public byte[] ToBytes()
        => new SectionWriter
        {
            TableId = TableId,
            TableIdExtension = ProgramNumber,
            Body = new ByteWriter()
                .Word(0xE000 | PcrPid)
                .Word(0xF000)
                .Run(Streams.SelectMany(stream => stream).ToArray())
                .ToArray(),
        }.ToBytes();
}
