namespace Carina.Broadcast.Tests.Building;

public sealed class TransportStreamWriter
{
    public const int PacketSize = 188;
    public const int HeaderSize = 4;
    public const int PayloadCapacity = PacketSize - HeaderSize;
    public const byte SyncByte = 0x47;
    public const byte StuffingByte = 0xFF;

    private readonly List<byte[]> packets = [];
    private readonly int pid;
    private int nextContinuityCounter;

    public TransportStreamWriter(int pid)
    {
        this.pid = pid;
    }

    public IReadOnlyList<byte[]> Packets => packets;

    public byte[] Bytes => packets.SelectMany(packet => packet).ToArray();

    public TransportStreamWriter Packet(
        int? pointerField,
        ReadOnlySpan<byte> payload,
        int adaptationFieldLength = -1,
        int? continuityCounter = null,
        bool transportError = false,
        int scramblingControl = 0)
    {
        var adaptation = adaptationFieldLength >= 0 ? adaptationFieldLength + 1 : 0;
        var pointer = pointerField is null ? 0 : 1;
        var capacity = PayloadCapacity - adaptation - pointer;

        if (payload.Length > capacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                payload.Length,
                $"A packet carries at most {capacity} bytes here.");
        }

        var counter = continuityCounter ?? nextContinuityCounter;
        nextContinuityCounter = (counter + 1) & 0x0F;

        var packet = new byte[PacketSize];
        Array.Fill(packet, StuffingByte);

        packet[0] = SyncByte;
        packet[1] = (byte)(((transportError ? 1 : 0) << 7) | ((pointerField is null ? 0 : 1) << 6) | (pid >> 8));
        packet[2] = (byte)(pid & 0xFF);
        packet[3] = (byte)((scramblingControl << 6) | ((adaptation > 0 ? 0b11 : 0b01) << 4) | counter);

        var at = HeaderSize;

        if (adaptation > 0)
        {
            packet[at++] = (byte)adaptationFieldLength;

            if (adaptationFieldLength > 0)
            {
                packet[at] = 0x00;
                at += adaptationFieldLength;
            }
        }

        if (pointerField is not null)
        {
            packet[at++] = (byte)pointerField.Value;
        }

        payload.CopyTo(packet.AsSpan(at));
        packets.Add(packet);

        return this;
    }

    public TransportStreamWriter AdaptationOnlyPacket(int? continuityCounter = null)
    {
        var counter = continuityCounter ?? nextContinuityCounter;

        var packet = new byte[PacketSize];
        Array.Fill(packet, StuffingByte);

        packet[0] = SyncByte;
        packet[1] = (byte)(pid >> 8);
        packet[2] = (byte)(pid & 0xFF);
        packet[3] = (byte)((0b10 << 4) | counter);
        packet[4] = PacketSize - HeaderSize - 1;
        packet[5] = 0x00;

        packets.Add(packet);

        return this;
    }

    public TransportStreamWriter Sections(params byte[][] sections)
        => Sections(adaptationFieldLength: -1, sections);

    public TransportStreamWriter Sections(int adaptationFieldLength, params byte[][] sections)
    {
        var joined = new List<byte>();
        var starts = new List<int>();

        foreach (var section in sections)
        {
            starts.Add(joined.Count);
            joined.AddRange(section);
        }

        var stream = joined.ToArray();
        var written = 0;

        while (written < stream.Length)
        {
            var adaptation = adaptationFieldLength >= 0 ? adaptationFieldLength + 1 : 0;
            var withoutPointer = PayloadCapacity - adaptation;
            var firstStart = starts.FirstOrDefault(start => start >= written, -1);
            int? pointer = null;
            var capacity = withoutPointer;

            if (firstStart >= written && firstStart - written < withoutPointer - 1)
            {
                pointer = firstStart - written;
                capacity = withoutPointer - 1;
            }

            var take = Math.Min(capacity, stream.Length - written);
            Packet(pointer, stream.AsSpan(written, take), adaptationFieldLength);
            written += take;
        }

        return this;
    }
}
