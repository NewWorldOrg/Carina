using Carina.Domain.Channels;
using Carina.Domain.Scans;

namespace Carina.Domain.Tests.Unit.Scans;

[Trait("Category", "Unit")]
public sealed class SatelliteTwinSlotsTests
{
    [Fact(DisplayName = "BR-TD-002: a BS slot carrying a stream an earlier slot carried is its twin, and is proposed once")]
    public void ABsSlotCarryingAnEarlierStreamIsATwin()
    {
        SatelliteTwinSlots twins = new();
        TuningParameters first = TuningParameters.Bs(1, new TransportStreamId(0x4010));
        TuningParameters second = TuningParameters.Bs(3, new TransportStreamId(0x4010));

        Assert.False(twins.TryFindTwin(first, 4, 0x4010, out _));
        Assert.True(twins.TryFindTwin(second, 4, 0x4010, out TuningParameters? twin));
        Assert.Equal(first, twin);
        Assert.False(twins.TryFindTwin(TuningParameters.Bs(5, new TransportStreamId(0x4011)), 4, 0x4011, out _));
    }

    [Fact(DisplayName = "BR-TD-002: a terrestrial stream heard on two channels is a relay, not a twin")]
    public void ATerrestrialStreamOnTwoChannelsIsNotATwin()
    {
        SatelliteTwinSlots twins = new();

        Assert.False(twins.TryFindTwin(TuningParameters.Terrestrial(27), 0x7FE0, 0x7FE0, out _));
        Assert.False(twins.TryFindTwin(TuningParameters.Terrestrial(45), 0x7FE0, 0x7FE0, out _));
    }
}
