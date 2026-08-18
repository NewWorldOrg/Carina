namespace Carina.BroadcastTestSupport;

public sealed class SectionWriter
{
    public required int TableId { get; init; }

    public int TableIdExtension { get; init; }

    public int VersionNumber { get; init; }

    public bool IsCurrent { get; init; } = true;

    public int SectionNumber { get; init; }

    public int LastSectionNumber { get; init; }

    public byte[] Body { get; init; } = [];

    public bool LongForm { get; init; } = true;

    public int? DeclaredLength { get; init; }

    public bool CorruptChecksum { get; init; }

    public byte[] ToBytes()
    {
        var section = new List<byte>();

        if (!LongForm)
        {
            int shortLength = DeclaredLength ?? Body.Length;

            section.Add((byte)TableId);
            section.Add((byte)(0b0111_0000 | (shortLength >> 8)));
            section.Add((byte)(shortLength & 0xFF));
            section.AddRange(Body);

            return section.ToArray();
        }

        int length = DeclaredLength ?? (Body.Length + 9);

        section.Add((byte)TableId);
        section.Add((byte)(0b1011_0000 | (length >> 8)));
        section.Add((byte)(length & 0xFF));
        section.Add((byte)(TableIdExtension >> 8));
        section.Add((byte)(TableIdExtension & 0xFF));
        section.Add((byte)(0b1100_0000 | (VersionNumber << 1) | (IsCurrent ? 1 : 0)));
        section.Add((byte)SectionNumber);
        section.Add((byte)LastSectionNumber);
        section.AddRange(Body);

        uint checksum = ReferenceCrc32.Compute(section.ToArray()) ^ (CorruptChecksum ? 1u : 0u);

        section.Add((byte)(checksum >> 24));
        section.Add((byte)(checksum >> 16));
        section.Add((byte)(checksum >> 8));
        section.Add((byte)checksum);

        return section.ToArray();
    }

    public static byte[] Filler(int length)
    {
        byte[] body = new byte[length];

        for (int at = 0; at < length; at++)
        {
            body[at] = (byte)(at & 0x7F);
        }

        return body;
    }
}
