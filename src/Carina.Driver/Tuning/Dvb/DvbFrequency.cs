using Carina.Contracts;

namespace Carina.Driver.Tuning.Dvb;

public static class DvbFrequency
{
    public const uint TerrestrialBandwidthHertz = 6_000_000;

    public static uint TerrestrialHertz(int physicalChannel) =>
        (uint)BroadcastStandards.TerrestrialCentreHz(physicalChannel);

    public static uint BroadcastSatelliteKilohertz(int slot) =>
        (uint)BroadcastStandards.BsCentreKHz(slot);

    public static uint CommunicationSatelliteKilohertz(int slot) =>
        (uint)BroadcastStandards.Cs110CentreKHz(slot);
}
