using Carina.Contracts;

namespace Carina.Domain.Channels;

public sealed class SatelliteTransportStream
{
    public const int FirstRelativeStreamNumber = 0;
    public const int LastRelativeStreamNumber = 7;

    private SatelliteTransportStream()
    {
    }

    public int BsChannel { get; private set; }

    public int RelativeStreamNumber { get; private set; }

    public TransportStreamId TransportStreamId { get; private set; } = null!;

    public static SatelliteTransportStream Rehydrate(
        int bsChannel,
        int relativeStreamNumber,
        TransportStreamId transportStreamId)
    {
        ArgumentNullException.ThrowIfNull(transportStreamId);

        if (!BroadcastStandards.IsBsChannel(bsChannel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bsChannel),
                bsChannel,
                $"A BS slot is an odd {BroadcastStandards.BsFirstChannel} to {BroadcastStandards.BsLastChannel}, less {string.Join(" and ", BroadcastStandards.BsChannelsWithoutDemodulation)}.");
        }

        if (relativeStreamNumber is < FirstRelativeStreamNumber or > LastRelativeStreamNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(relativeStreamNumber),
                relativeStreamNumber,
                $"A relative stream number is {FirstRelativeStreamNumber} to {LastRelativeStreamNumber}.");
        }

        return new SatelliteTransportStream
        {
            BsChannel = bsChannel,
            RelativeStreamNumber = relativeStreamNumber,
            TransportStreamId = transportStreamId,
        };
    }

    public TuningParameters ToTuningParameters()
        => TuningParameters.Bs(BsChannel, TransportStreamId);
}
