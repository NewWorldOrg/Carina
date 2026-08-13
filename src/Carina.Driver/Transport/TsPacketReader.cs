namespace Carina.Driver.Transport;

public readonly record struct TsPacket(
    int Pid,
    int ContinuityCounter,
    bool HasPayload,
    bool TransportError = false,
    bool Scrambled = false,
    bool Discontinuity = false,
    bool PayloadUnitStart = false,
    int PayloadHash = 0
)
{
    public const int NullPid = 0x1FFF;
    public const int MaxPid = 0x1FFF;
    public const int CounterWrap = 16;

    public bool IsNull => Pid is NullPid;
}

public sealed class TsPacketReader
{
    public const int PacketLength = 188;
    public const byte SyncByte = 0x47;

    private const int HeaderLength = 4;

    private readonly List<byte> buffer = [];
    private bool aligned;
    private bool confirmed;

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
                DiscardAllButATrailingPacket();
                break;
            }

            if (buffer.Count < PacketLength)
            {
                break;
            }

            if (buffer[0] is not SyncByte)
            {
                aligned = false;
                confirmed = false;
                ResyncCount++;
                continue;
            }

            if (!confirmed && buffer.Count < PacketLength * 2)
            {
                break;
            }

            packets.Add(ReadHeader());
            PacketsRead++;
            buffer.RemoveRange(0, PacketLength);
            confirmed = true;
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
            confirmed = next < buffer.Count + offset;

            return true;
        }

        return false;
    }

    private void DiscardAllButATrailingPacket()
    {
        var keep = PacketLength - 1;
        if (buffer.Count <= keep)
        {
            return;
        }

        var drop = buffer.Count - keep;
        buffer.RemoveRange(0, drop);
        DiscardedBytes += drop;
    }

    private TsPacket ReadHeader()
    {
        var transportError = (buffer[1] & 0x80) is not 0;
        var payloadUnitStart = (buffer[1] & 0x40) is not 0;
        var pid = ((buffer[1] & 0x1F) << 8) | buffer[2];
        var scrambling = (buffer[3] >> 6) & 0x03;
        var adaptationField = (buffer[3] >> 4) & 0x03;
        var hasAdaptation = adaptationField is 0x02 or 0x03;
        var hasPayload = adaptationField is 0x01 or 0x03;

        var discontinuity = false;
        if (hasAdaptation && buffer[HeaderLength] > 0)
        {
            discontinuity = (buffer[HeaderLength + 1] & 0x80) is not 0;
        }

        return new TsPacket(
            pid,
            buffer[3] & 0x0F,
            hasPayload,
            transportError,
            Scrambled: scrambling is not 0,
            discontinuity,
            payloadUnitStart,
            PayloadHash: HashPayload()
        );
    }

    private int HashPayload()
    {
        var hash = 17;
        for (var index = HeaderLength; index < PacketLength; index++)
        {
            hash = (hash * 31) + buffer[index];
        }

        return hash;
    }

    public bool IsAlignmentConfirmed => confirmed;

    public bool LooksLikeAnotherStride =>
        DiscardedBytes > PacketLength && DiscardedBytes > PacketsRead * PacketLength;

    public long PacketsRead { get; private set; }
}
