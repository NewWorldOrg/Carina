namespace Carina.Driver.Transport;

public readonly record struct TsPacket(int Pid, int ContinuityCounter, bool HasPayload)
{
    public const int NullPid = 0x1FFF;

    public bool IsNull => Pid is NullPid;
}

public sealed class TsPacketReader
{
    public const int PacketLength = 188;

    private const byte SyncByte = 0x47;

    private readonly List<byte> buffer = [];
    private bool aligned;

    public int ResyncCount { get; private set; }

    public long DiscardedBytes { get; private set; }

    public IReadOnlyList<TsPacket> Read(ReadOnlySpan<byte> bytes)
    {
        buffer.AddRange(bytes);

        var packets = new List<TsPacket>();
        while (true)
        {
            if (!aligned && !TryAlign())
            {
                break;
            }

            if (buffer.Count < PacketLength)
            {
                break;
            }

            if (buffer[0] is not SyncByte)
            {
                aligned = false;
                ResyncCount++;
                continue;
            }

            packets.Add(ReadHeader());
            buffer.RemoveRange(0, PacketLength);
        }

        return packets;
    }

    private bool TryAlign()
    {
        for (var offset = 0; offset + PacketLength <= buffer.Count; offset++)
        {
            if (buffer[offset] is not SyncByte)
            {
                continue;
            }

            var next = offset + PacketLength;
            if (next < buffer.Count && buffer[next] is not SyncByte)
            {
                continue;
            }

            buffer.RemoveRange(0, offset);
            DiscardedBytes += offset;
            aligned = true;
            return true;
        }

        return false;
    }

    private TsPacket ReadHeader()
    {
        var pid = ((buffer[1] & 0x1F) << 8) | buffer[2];
        var adaptationField = (buffer[3] >> 4) & 0x03;

        return new TsPacket(
            pid,
            buffer[3] & 0x0F,
            HasPayload: adaptationField is 0x01 or 0x03
        );
    }
}
