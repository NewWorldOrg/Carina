using Carina.Domain.Channels;

namespace Carina.Infrastructure.Persistence.Configurations;

internal static class SatelliteTransportStreamSeed
{
    public const int FirstStreamOfTheTransponder = 0;

    public static readonly IReadOnlyList<SatelliteTransportStream> Rows =
    [
        Row(1, 0x4010),
        Row(3, 0x4030),
        Row(5, 0x4050),
        Row(9, 0x4090),
        Row(11, 0x40B0),
        Row(13, 0x40D0),
        Row(15, 0x40F0),
        Row(19, 0x4130),
        Row(21, 0x4150),
        Row(23, 0x4170),
    ];

    private static SatelliteTransportStream Row(int bsChannel, int transportStreamId)
        => SatelliteTransportStream.Rehydrate(
            bsChannel,
            FirstStreamOfTheTransponder,
            new TransportStreamId(transportStreamId));
}
