namespace Carina.BroadcastTestSupport;

public static class DsmCcWriter
{
    public const int DownloadInfoIndicationTableId = 0x3B;

    public const int DownloadDataBlockTableId = 0x3C;

    public const int StreamDescriptorsTableId = 0x3D;

    public const int DownloadInfoIndicationMessageId = 0x1002;

    public const int DownloadDataBlockMessageId = 0x1003;

    public const int DownloadServerInitiateMessageId = 0x1006;

    public const int ProtocolDiscriminator = 0x11;

    public const int DownloadType = 0x03;

    public const int LargestBlock = 4066;

    public const int MessageHeaderSize = 12;

    public static byte[] Message(
        int messageId,
        long identifier,
        byte[] payload,
        byte[]? adaptation = null,
        int? declaredMessageLength = null,
        int? declaredAdaptationLength = null,
        int protocolDiscriminator = ProtocolDiscriminator,
        int dsmccType = DownloadType)
    {
        ArgumentNullException.ThrowIfNull(payload);
        byte[] header = adaptation ?? [];

        return new ByteWriter()
            .Byte(protocolDiscriminator)
            .Byte(dsmccType)
            .Word(messageId)
            .DoubleWord(identifier)
            .Byte(0xFF)
            .Byte(declaredAdaptationLength ?? header.Length)
            .Word(declaredMessageLength ?? (header.Length + payload.Length))
            .Run(header)
            .Run(payload)
            .ToArray();
    }

    public static IReadOnlyList<SectionWriter> Blocks(long downloadId, int moduleId, int moduleVersion, byte[] module, int blockSize)
    {
        ArgumentNullException.ThrowIfNull(module);
        var blocks = new List<SectionWriter>();
        int count = Math.Max(1, (module.Length + blockSize - 1) / blockSize);

        for (int number = 0; number < count; number++)
        {
            int start = number * blockSize;
            int length = Math.Min(blockSize, module.Length - start);

            blocks.Add(new DdbWriter
            {
                DownloadId = downloadId,
                ModuleId = moduleId,
                ModuleVersion = moduleVersion,
                BlockNumber = number,
                Data = module.AsSpan(start, length).ToArray(),
            }.ToSection());
        }

        return blocks;
    }
}
