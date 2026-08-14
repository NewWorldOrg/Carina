using Carina.Driver.Transport;

namespace Carina.Driver.Tuning;

public sealed class FakeTunerDevice : ITunerDevice
{
    private const byte SyncByte = 0x47;
    private const int VideoPid = 0x0100;

    private readonly byte seed;
    private int continuityCounter;
    private long packetsProduced;
    private int offsetInPacket;

    public FakeTunerDevice(int physicalChannel, int? serviceId = null)
    {
        seed = unchecked((byte)((physicalChannel * 31) + (serviceId ?? 0)));
    }

    public long Overflows => 0;

    public byte[] Read(int count, CancellationToken cancellationToken)
    {
        var bytes = new byte[count];

        for (var written = 0; written < count; written++)
        {
            bytes[written] = ByteAt(offsetInPacket);

            offsetInPacket++;
            if (offsetInPacket is TsPacketReader.PacketLength)
            {
                offsetInPacket = 0;
                packetsProduced++;
                continuityCounter = (continuityCounter + 1) % 16;
            }
        }

        return bytes;
    }

    private byte ByteAt(int offset) =>
        offset switch
        {
            0 => SyncByte,
            1 => (byte)((VideoPid >> 8) & 0x1F),
            2 => VideoPid & 0xFF,
            3 => (byte)(0x10 | continuityCounter),
            _ => unchecked((byte)(seed + offset + packetsProduced)),
        };

    public void Dispose() { }
}
