namespace Carina.Driver.Tuning.Dvb;

public abstract record DvbChannel
{
    private protected DvbChannel() { }

    public abstract bool NeedsSatelliteAerial { get; }

    public static DvbChannel Terrestrial(int physicalChannel)
    {
        if (
            physicalChannel < DvbFrequency.LowestTerrestrialChannel
            || physicalChannel > DvbFrequency.HighestTerrestrialChannel
        )
        {
            throw DvbFailure.Refused(
                $"physicalChannel: terrestrial broadcasting occupies channels {DvbFrequency.LowestTerrestrialChannel} to {DvbFrequency.HighestTerrestrialChannel}, and {physicalChannel} is outside that plan, so no frequency can be derived for it."
            );
        }

        return new TerrestrialChannel(physicalChannel);
    }

    public static DvbChannel BroadcastSatellite(int slot, int? transportStreamId)
    {
        if (
            slot < DvbFrequency.LowestBroadcastSatelliteSlot
            || slot > DvbFrequency.HighestBroadcastSatelliteSlot
            || slot % 2 is 0
        )
        {
            throw DvbFailure.Refused(
                $"slot: broadcast satellite occupies the odd slots {DvbFrequency.LowestBroadcastSatelliteSlot} to {DvbFrequency.HighestBroadcastSatelliteSlot}, and {slot} is not one of them."
            );
        }

        if (DvbFrequency.UndemodulatableBroadcastSatelliteSlots.Contains(slot))
        {
            throw DvbFailure.Refused(
                $"slot: broadcast satellite slot {slot} cannot be demodulated by this class of tuner, so it is not a channel this driver will tune."
            );
        }

        if (transportStreamId is { } stream && stream is < 0 or > ushort.MaxValue)
        {
            throw DvbFailure.Refused(
                $"transportStreamId: a transport stream identifier is a sixteen bit number, and {stream} is not one."
            );
        }

        return new BroadcastSatelliteChannel(slot, transportStreamId);
    }

    public static DvbChannel CommunicationSatellite(int slot)
    {
        if (
            slot < DvbFrequency.LowestCommunicationSatelliteSlot
            || slot > DvbFrequency.HighestCommunicationSatelliteSlot
            || slot % 2 is not 0
        )
        {
            throw DvbFailure.Refused(
                $"slot: communication satellite occupies the even slots {DvbFrequency.LowestCommunicationSatelliteSlot} to {DvbFrequency.HighestCommunicationSatelliteSlot}, and {slot} is not one of them."
            );
        }

        return new CommunicationSatelliteChannel(slot);
    }
}

public sealed record TerrestrialChannel(int PhysicalChannel) : DvbChannel
{
    public override bool NeedsSatelliteAerial => false;
}

public sealed record BroadcastSatelliteChannel(int Slot, int? TransportStreamId) : DvbChannel
{
    public override bool NeedsSatelliteAerial => true;
}

public sealed record CommunicationSatelliteChannel(int Slot) : DvbChannel
{
    public override bool NeedsSatelliteAerial => true;
}
