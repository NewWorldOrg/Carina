using Carina.Driver.Transport;

namespace Carina.Driver.Tuning;

/// <summary>
/// A tuner that needs no hardware, producing the same stream every time.
/// </summary>
/// <remarks>
/// Everything above the device — sessions, recording, the continuity measurement —
/// can then be developed, tested and accepted on a machine with no card in it. The
/// stream is derived from what was asked for, so a test that reads it gets the same
/// bytes on every run and a failure means something changed rather than that the
/// weather did.
///
/// The packets are well formed and unbroken on purpose: this device shows what a
/// healthy stream looks like. Producing a damaged one is a separate device, so that
/// "the measurement found nothing" and "the stream had nothing to find" stay
/// distinguishable.
/// </remarks>
public sealed class FakeTunerDevice
{
    private const byte SyncByte = 0x47;
    private const int VideoPid = 0x0100;

    private readonly byte seed;
    private int continuityCounter;
    private long packetsProduced;
    private int offsetInPacket;

    /// <summary>Creates a device for one tuning request.</summary>
    /// <param name="physicalChannel">The channel that was asked for.</param>
    /// <param name="serviceId">The service that was asked for, when there was one.</param>
    public FakeTunerDevice(int physicalChannel, int? serviceId = null)
    {
        seed = unchecked((byte)((physicalChannel * 31) + (serviceId ?? 0)));
    }

    /// <summary>Reads up to <paramref name="count"/> bytes of the stream.</summary>
    /// <remarks>
    /// A read may stop in the middle of a packet, because a real one does when the
    /// buffer runs out. The next read continues from there.
    /// </remarks>
    public byte[] Read(int count)
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
            // Payload present, no adaptation field, and the counter the measurement
            // above is going to check.
            3 => (byte)(0x10 | continuityCounter),
            _ => unchecked((byte)(seed + offset + packetsProduced)),
        };
}
