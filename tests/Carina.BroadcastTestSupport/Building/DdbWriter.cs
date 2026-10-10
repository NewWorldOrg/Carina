namespace Carina.BroadcastTestSupport;

public sealed class DdbWriter
{
    public long DownloadId { get; init; } = 0x0000_0001;

    public int ModuleId { get; init; }

    public int ModuleVersion { get; init; }

    public int BlockNumber { get; init; }

    public byte[] Data { get; init; } = [];

    public int? DeclaredMessageLength { get; init; }

    public int MessageId { get; init; } = DsmCcWriter.DownloadDataBlockMessageId;

    public byte[] ToMessage()
        => DsmCcWriter.Message(
            MessageId,
            DownloadId,
            new ByteWriter()
                .Word(ModuleId)
                .Byte(ModuleVersion)
                .Byte(0xFF)
                .Word(BlockNumber)
                .Run(Data)
                .ToArray(),
            declaredMessageLength: DeclaredMessageLength);

    public SectionWriter ToSection() => Section(ToMessage());

    public SectionWriter Section(byte[] body)
        => new()
        {
            TableId = DsmCcWriter.DownloadDataBlockTableId,
            TableIdExtension = ModuleId,
            VersionNumber = ModuleVersion & 0x1F,
            SectionNumber = BlockNumber & 0xFF,
            LastSectionNumber = 0xFF,
            Body = body,
        };
}
