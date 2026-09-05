namespace Carina.BroadcastTestSupport;

public sealed class CdtWriter
{
    public int OriginalNetworkId { get; init; }

    public int DataType { get; init; } = 0x01;

    public byte[] Descriptors { get; init; } = [];

    public int? DeclaredDescriptorsLength { get; init; }

    public byte[] DataModule { get; init; } = [];

    public byte[] ToBody()
    {
        int length = DeclaredDescriptorsLength ?? Descriptors.Length;

        return new ByteWriter()
            .Word(OriginalNetworkId)
            .Byte(DataType)
            .Byte(0xF0 | (length >> 8))
            .Byte(length & 0xFF)
            .Run(Descriptors)
            .Run(DataModule)
            .ToArray();
    }

    public static byte[] LogoModule(
        int logoType,
        int logoId,
        int logoVersion,
        byte[] picture,
        int? declaredSize = null)
        => new ByteWriter()
            .Byte(logoType)
            .Byte(0xFE | (logoId >> 8))
            .Byte(logoId & 0xFF)
            .Byte(0xF0 | (logoVersion >> 8))
            .Byte(logoVersion & 0xFF)
            .Word(declaredSize ?? picture.Length)
            .Run(picture)
            .ToArray();
}
