using Carina.Contracts;

namespace Carina.Driver.Tuning.Dvb;

public abstract record DvbChannel
{
    private protected DvbChannel() { }

    public abstract bool NeedsSatelliteAerial { get; }

    public static DvbChannel Terrestrial(int physicalChannel)
    {
        if (!BroadcastStandards.IsTerrestrialChannel(physicalChannel))
        {
            throw DvbFailure.Refused(
                $"physicalChannel: terrestrial broadcasting occupies channels {BroadcastStandards.TerrestrialFirstChannel} to {BroadcastStandards.TerrestrialLastChannel}, and {physicalChannel} is outside that plan, so no frequency can be derived for it."
            );
        }

        return new TerrestrialChannel(physicalChannel);
    }

    public static DvbChannel BroadcastSatellite(int slot, int? transportStreamId)
    {
        if (!BroadcastStandards.IsBsChannel(slot))
        {
            throw DvbFailure.Refused(
                BroadcastStandards.BsChannelsWithoutDemodulation.Contains(slot)
                    ? $"slot: broadcast satellite slot {slot} cannot be demodulated by this class of tuner, so it is not a channel this driver will tune."
                    : $"slot: broadcast satellite occupies the odd slots {BroadcastStandards.BsFirstChannel} to {BroadcastStandards.BsLastChannel}, and {slot} is not one of them."
            );
        }

        if (transportStreamId is not { } stream)
        {
            throw DvbFailure.Refused(
                $"transportStreamId: broadcast satellite slot {slot} can carry more than one transport stream, and a slot carrying none of its own answers with the first stream on the transponder, so a tune that does not name the transport stream it wants cannot tell what it received from what it asked for."
            );
        }

        if (!BroadcastStandards.IsTransportStreamId(stream))
        {
            throw DvbFailure.Refused(
                $"transportStreamId: a transport stream identifier runs from {BroadcastStandards.MinTransportStreamId} to {BroadcastStandards.MaxTransportStreamId}, and {stream} is outside it."
            );
        }

        return new BroadcastSatelliteChannel(slot, stream);
    }

    public static DvbChannel CommunicationSatellite(int slot)
    {
        if (!BroadcastStandards.IsCs110Channel(slot))
        {
            throw DvbFailure.Refused(
                $"slot: communication satellite occupies the even slots {BroadcastStandards.Cs110FirstChannel} to {BroadcastStandards.Cs110LastChannel}, and {slot} is not one of them."
            );
        }

        return new CommunicationSatelliteChannel(slot);
    }
}

public sealed record TerrestrialChannel(int PhysicalChannel) : DvbChannel
{
    public override bool NeedsSatelliteAerial => false;
}

public sealed record BroadcastSatelliteChannel(int Slot, int TransportStreamId) : DvbChannel
{
    public override bool NeedsSatelliteAerial => true;
}

public sealed record CommunicationSatelliteChannel(int Slot) : DvbChannel
{
    public override bool NeedsSatelliteAerial => true;
}
