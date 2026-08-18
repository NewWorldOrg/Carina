using Carina.Driver.Transport;
using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tuning;

public sealed class FakeTunerDevice : ITunerDevice, ISignalQualitySource
{
    private const byte SyncByte = 0x47;
    private const int VideoPid = 0x0100;

    private const double SyntheticDecibels = 20;

    private static readonly SignalQuality Synthetic = new(
        LockWindow.Throughout(
            FrontendStatus.Signal
                | FrontendStatus.Carrier
                | FrontendStatus.Viterbi
                | FrontendStatus.Sync
                | FrontendStatus.Lock
        ),
        CarrierToNoise.Measured(SyntheticDecibels),
        new PostViterbiErrors(
            SignalReading.Measured,
            [new LayerBitErrors(0, 0, 1_000_000), new LayerBitErrors(1, 0, 500_000)]
        )
    );

    private readonly byte seed;
    private int continuityCounter;
    private long packetsProduced;
    private int offsetInPacket;

    public FakeTunerDevice(int physicalChannel, int? serviceId = null)
    {
        seed = unchecked((byte)((physicalChannel * 31) + (serviceId ?? 0)));
    }

    public long Overflows => 0;

    public ISignalQualitySource? Quality => this;

    public SignalQuality Measure() => Synthetic;

    public byte[] Read(int count, CancellationToken cancellationToken)
    {
        byte[] bytes = new byte[count];

        for (int written = 0; written < count; written++)
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
