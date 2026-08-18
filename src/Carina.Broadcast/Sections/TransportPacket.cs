namespace Carina.Broadcast.Sections;

public readonly ref struct TransportPacket
{
    public const int Size = 188;

    public const int HeaderSize = 4;

    public const byte SyncByte = 0x47;

    public const int NullPacketPid = 0x1FFF;

    private TransportPacket(
        int pid,
        bool transportError,
        bool payloadUnitStart,
        bool isScrambled,
        bool hasAdaptationField,
        bool hasPayload,
        int continuityCounter,
        ReadOnlySpan<byte> payload)
    {
        Pid = pid;
        TransportError = transportError;
        PayloadUnitStart = payloadUnitStart;
        IsScrambled = isScrambled;
        HasAdaptationField = hasAdaptationField;
        HasPayload = hasPayload;
        ContinuityCounter = continuityCounter;
        Payload = payload;
    }

    public int Pid { get; }

    public bool TransportError { get; }

    public bool PayloadUnitStart { get; }

    public bool IsScrambled { get; }

    public bool HasAdaptationField { get; }

    public bool HasPayload { get; }

    public int ContinuityCounter { get; }

    public ReadOnlySpan<byte> Payload { get; }

    public static bool TryRead(ReadOnlySpan<byte> packet, out TransportPacket read)
    {
        read = default;

        if (packet.Length != Size || packet[0] != SyncByte)
        {
            return false;
        }

        int adaptationFieldControl = (packet[3] >> 4) & 0b11;
        bool hasAdaptationField = (adaptationFieldControl & 0b10) != 0;
        bool hasPayload = (adaptationFieldControl & 0b01) != 0;
        int payloadStart = HeaderSize;

        if (hasAdaptationField)
        {
            byte adaptationFieldLength = packet[HeaderSize];
            payloadStart = HeaderSize + 1 + adaptationFieldLength;

            if (payloadStart > Size)
            {
                return false;
            }
        }

        read = new TransportPacket(
            ((packet[1] & 0x1F) << 8) | packet[2],
            (packet[1] & 0x80) != 0,
            (packet[1] & 0x40) != 0,
            (packet[3] & 0xC0) != 0,
            hasAdaptationField,
            hasPayload,
            packet[3] & 0x0F,
            hasPayload ? packet[payloadStart..] : []);

        return true;
    }
}
