namespace Carina.Driver.Tuning.Dvb;

public static class DvbFrequency
{
    public const int LowestTerrestrialChannel = 13;
    public const int HighestTerrestrialChannel = 62;

    public const int LowestBroadcastSatelliteSlot = 1;
    public const int HighestBroadcastSatelliteSlot = 23;

    public const int LowestCommunicationSatelliteSlot = 2;
    public const int HighestCommunicationSatelliteSlot = 24;

    public const uint TerrestrialBandwidthHertz = 6_000_000;

    private const long TerrestrialBandStartHertz = 470_000_000;
    private const long TerrestrialChannelWidthHertz = 6_000_000;
    private const long TerrestrialChannelCentreHertz = TerrestrialChannelWidthHertz / 2;
    private const long TerrestrialCarrierOffsetHertz = 1_000_000 / 7;

    private const long LowestBroadcastSatelliteKilohertz = 1_049_480;
    private const long BroadcastSatelliteSlotSpacingKilohertz = 38_360;

    private const long LowestCommunicationSatelliteKilohertz = 1_613_000;
    private const long CommunicationSatelliteSlotSpacingKilohertz = 40_000;

    public static readonly IReadOnlySet<int> UndemodulatableBroadcastSatelliteSlots =
        new HashSet<int> { 7, 17 };

    public static uint TerrestrialHertz(int physicalChannel) =>
        (uint)(
            TerrestrialBandStartHertz
            + TerrestrialChannelCentreHertz
            + TerrestrialCarrierOffsetHertz
            + ((physicalChannel - LowestTerrestrialChannel) * TerrestrialChannelWidthHertz)
        );

    public static uint BroadcastSatelliteKilohertz(int slot) =>
        (uint)(
            LowestBroadcastSatelliteKilohertz
            + (
                ((slot - LowestBroadcastSatelliteSlot) / 2)
                * BroadcastSatelliteSlotSpacingKilohertz
            )
        );

    public static uint CommunicationSatelliteKilohertz(int slot) =>
        (uint)(
            LowestCommunicationSatelliteKilohertz
            + (
                ((slot - LowestCommunicationSatelliteSlot) / 2)
                * CommunicationSatelliteSlotSpacingKilohertz
            )
        );
}
