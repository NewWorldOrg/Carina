namespace Carina.BroadcastTestSupport;

public sealed class DiiWriter
{
    public long TransactionId { get; init; } = 0x8000_0002;

    public long DownloadId { get; init; } = 0x0000_0001;

    public int BlockSize { get; init; } = DsmCcWriter.LargestBlock;

    public IReadOnlyList<DiiModule> Modules { get; init; } = [];

    public int? DeclaredModuleCount { get; init; }

    public byte[] Compatibility { get; init; } = [];

    public byte[] PrivateData { get; init; } = [];

    public int? DeclaredPrivateDataLength { get; init; }

    public int MessageId { get; init; } = DsmCcWriter.DownloadInfoIndicationMessageId;

    public byte[] ToMessage()
    {
        ByteWriter payload = new ByteWriter()
            .DoubleWord(DownloadId)
            .Word(BlockSize)
            .Byte(0x00)
            .Byte(0x00)
            .DoubleWord(0)
            .DoubleWord(0)
            .Word(Compatibility.Length)
            .Run(Compatibility)
            .Word(DeclaredModuleCount ?? Modules.Count);

        foreach (DiiModule module in Modules)
        {
            payload
                .Word(module.ModuleId)
                .DoubleWord(module.Size)
                .Byte(module.Version)
                .Byte(module.DeclaredInfoLength ?? module.Info.Length)
                .Run(module.Info);
        }

        payload.Word(DeclaredPrivateDataLength ?? PrivateData.Length).Run(PrivateData);

        return DsmCcWriter.Message(MessageId, TransactionId, payload.ToArray());
    }

    public SectionWriter ToSection() => Section(ToMessage());

    public SectionWriter Section(byte[] body)
        => new()
        {
            TableId = DsmCcWriter.DownloadInfoIndicationTableId,
            TableIdExtension = (int)(TransactionId & 0xFFFF),
            Body = body,
        };
}

public sealed record DiiModule(int ModuleId, long Size, int Version, byte[] Info)
{
    public int? DeclaredInfoLength { get; init; }

    public static DiiModule Of(int moduleId, long size, int version, params byte[][] descriptors)
        => new(moduleId, size, version, DescriptorWriter.Loop(descriptors));
}
