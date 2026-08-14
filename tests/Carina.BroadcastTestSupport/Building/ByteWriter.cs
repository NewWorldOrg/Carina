namespace Carina.BroadcastTestSupport;

public sealed class ByteWriter
{
    private readonly List<byte> bytes = [];

    public int Count => bytes.Count;

    public ByteWriter Byte(int value)
    {
        bytes.Add((byte)value);

        return this;
    }

    public ByteWriter Word(int value)
    {
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)(value & 0xFF));

        return this;
    }

    public ByteWriter Run(ReadOnlySpan<byte> run)
    {
        foreach (var value in run)
        {
            bytes.Add(value);
        }

        return this;
    }

    public byte[] ToArray() => bytes.ToArray();
}
