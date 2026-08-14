namespace Carina.Broadcast.Sections;

public sealed class Section
{
    public const int LengthPrefixSize = 3;

    public const int HeaderSize = 8;

    public const int MinimumLongFormLength = 9;

    public const int MaximumDeclaredLength = 4093;

    private readonly byte[] raw;

    private Section(byte[] raw)
    {
        this.raw = raw;
        TableId = raw[0];
        TableIdExtension = (raw[3] << 8) | raw[4];
        VersionNumber = (raw[5] >> 1) & 0x1F;
        IsCurrent = (raw[5] & 0x01) != 0;
        SectionNumber = raw[6];
        LastSectionNumber = raw[7];
    }

    public int TableId { get; }

    public int TableIdExtension { get; }

    public int VersionNumber { get; }

    public bool IsCurrent { get; }

    public int SectionNumber { get; }

    public int LastSectionNumber { get; }

    public ReadOnlyMemory<byte> Body
        => raw.AsMemory(HeaderSize, raw.Length - HeaderSize - Crc32Mpeg.ChecksumSize);

    public ReadOnlyMemory<byte> Bytes => raw;

    internal static Section Over(byte[] raw) => new(raw);
}
