namespace Carina.Driver.Transport;

public readonly record struct TsPacket(
    int Pid,
    int ContinuityCounter,
    bool HasPayload,
    bool TransportError = false,
    bool Scrambled = false,
    bool Discontinuity = false,
    bool PayloadUnitStart = false,
    int PayloadHash = 0,
    bool Provisional = false,
    long? Pcr = null
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

    private const int PcrFieldLength = 7;

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

            if (!confirmed && buffer.Count >= PacketLength * 2)
            {
                if (buffer[PacketLength] is not SyncByte)
                {
                    buffer.RemoveRange(0, 1);
                    DiscardedBytes++;
                    aligned = false;
                    continue;
                }

                confirmed = true;
            }

            packets.Add(ReadHeader() with { Provisional = !confirmed });
            PacketsRead++;
            buffer.RemoveRange(0, PacketLength);
            confirmed = true;
        }

        return packets;
    }

    private bool TryAlign()
    {
        for (int offset = 0; offset + PacketLength <= buffer.Count; offset++)
        {
            if (buffer[offset] is not SyncByte)
            {
                continue;
            }

            int next = offset + PacketLength;
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
        int keep = PacketLength - 1;
        if (buffer.Count <= keep)
        {
            return;
        }

        int drop = buffer.Count - keep;
        buffer.RemoveRange(0, drop);
        DiscardedBytes += drop;
    }

    private TsPacket ReadHeader()
    {
        bool transportError = (buffer[1] & 0x80) is not 0;
        bool payloadUnitStart = (buffer[1] & 0x40) is not 0;
        int pid = ((buffer[1] & 0x1F) << 8) | buffer[2];
        int scrambling = (buffer[3] >> 6) & 0x03;
        int adaptationField = (buffer[3] >> 4) & 0x03;
        bool hasAdaptation = adaptationField is 0x02 or 0x03;
        bool hasPayload = adaptationField is 0x01 or 0x03;

        bool discontinuity = false;
        long? pcr = null;
        if (hasAdaptation && buffer[HeaderLength] > 0)
        {
            discontinuity = (buffer[HeaderLength + 1] & 0x80) is not 0;
            pcr = ReadProgrammeClock();
        }

        return new TsPacket(
            pid,
            buffer[3] & 0x0F,
            hasPayload,
            transportError,
            Scrambled: scrambling is not 0,
            discontinuity,
            payloadUnitStart,
            PayloadHash: HashPayload(hasAdaptation),
            Pcr: pcr
        );
    }

    private long? ReadProgrammeClock()
    {
        if ((buffer[HeaderLength + 1] & 0x10) is 0 || buffer[HeaderLength] < PcrFieldLength)
        {
            return null;
        }

        return ((long)buffer[HeaderLength + 2] << 25)
            | ((long)buffer[HeaderLength + 3] << 17)
            | ((long)buffer[HeaderLength + 4] << 9)
            | ((long)buffer[HeaderLength + 5] << 1)
            | ((long)buffer[HeaderLength + 6] >> 7);
    }

    private int HashPayload(bool hasAdaptation)
    {
        int start = HeaderLength;
        if (hasAdaptation)
        {
            start += 1 + buffer[HeaderLength];
        }

        int hash = 17;
        for (int index = Math.Min(start, PacketLength); index < PacketLength; index++)
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
