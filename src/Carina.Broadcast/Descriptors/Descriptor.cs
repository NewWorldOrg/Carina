namespace Carina.Broadcast.Descriptors;

public sealed class Descriptor
{
    public const int HeaderSize = 2;

    internal Descriptor(int tag, ReadOnlyMemory<byte> payload)
    {
        Tag = tag;
        Payload = payload;
    }

    public int Tag { get; }

    public ReadOnlyMemory<byte> Payload { get; }
}
