using Carina.Contracts;

namespace Carina.Domain.Channels;

public sealed record TuningParameters
{
    private TuningParameters(TuneSystem system, int physicalChannel, TransportStreamId? transportStreamId)
    {
        System = system;
        PhysicalChannel = physicalChannel;
        TransportStreamId = transportStreamId;
    }

    public TuneSystem System { get; }

    public int PhysicalChannel { get; }

    public TransportStreamId? TransportStreamId { get; }

    public static TuningParameters Terrestrial(int physicalChannel)
    {
        if (!BroadcastStandards.IsTerrestrialChannel(physicalChannel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(physicalChannel),
                physicalChannel,
                $"A terrestrial channel is {BroadcastStandards.TerrestrialFirstChannel} to {BroadcastStandards.TerrestrialLastChannel}.");
        }

        return new TuningParameters(TuneSystem.IsdbT, physicalChannel, null);
    }

    public static TuningParameters Bs(int bsChannel, TransportStreamId transportStreamId)
    {
        ArgumentNullException.ThrowIfNull(transportStreamId);

        if (!BroadcastStandards.IsBsChannel(bsChannel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bsChannel),
                bsChannel,
                $"A BS slot is an odd {BroadcastStandards.BsFirstChannel} to {BroadcastStandards.BsLastChannel}, less {string.Join(" and ", BroadcastStandards.BsChannelsWithoutDemodulation)}.");
        }

        return new TuningParameters(TuneSystem.IsdbSBs, bsChannel, transportStreamId);
    }

    public static TuningParameters Cs110(int csChannel)
    {
        if (!BroadcastStandards.IsCs110Channel(csChannel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(csChannel),
                csChannel,
                $"A CS110 slot is an even {BroadcastStandards.Cs110FirstChannel} to {BroadcastStandards.Cs110LastChannel}.");
        }

        return new TuningParameters(TuneSystem.IsdbSCs110, csChannel, null);
    }
}
