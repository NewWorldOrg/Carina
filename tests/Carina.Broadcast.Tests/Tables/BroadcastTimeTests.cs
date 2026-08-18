using Carina.Broadcast.Tables;

namespace Carina.Broadcast.Tests.Tables;

public sealed class BroadcastTimeTests
{
    [Fact]
    public void AStartIsReadAsTheLocalTimeTheBroadcastMeans()
    {
        Assert.True(BroadcastTime.TryReadStart([0xEF, 0x55, 0x22, 0x57, 0x00], out var start));

        Assert.Equal(new DateTimeOffset(2026, 8, 17, 22, 57, 0, TimeSpan.FromHours(9)), start);
    }

    [Fact]
    public void AnHourNoClockShowsIsRefusedRatherThanRolledIntoTheNextDay()
    {
        Assert.False(BroadcastTime.TryReadStart([0xEF, 0x55, 0x25, 0x00, 0x00], out var start));

        Assert.Null(start);
    }

    [Fact]
    public void ADurationMayRunLongerThanADayEvenThoughAStartMayNot()
    {
        Assert.True(BroadcastTime.TryReadDuration([0x30, 0x00, 0x00], out var runs));

        Assert.Equal(TimeSpan.FromHours(30), runs);
    }

    [Fact]
    public void AClockThatIsNotPackedDecimalIsRefused()
    {
        Assert.False(BroadcastTime.TryReadStart([0xEF, 0x55, 0x2A, 0x00, 0x00], out _));
        Assert.False(BroadcastTime.TryReadStart([0xEF, 0x55, 0x22, 0x60, 0x00], out _));
        Assert.False(BroadcastTime.TryReadStart([0xEF, 0x55, 0x22, 0x00, 0x60], out _));
    }

    [Fact]
    public void AStartCutShortIsRefusedRatherThanGuessed()
    {
        Assert.False(BroadcastTime.TryReadStart([0xEF, 0x55, 0x22, 0x57], out _));
    }

    [Fact]
    public void ADurationIsReadAsALengthOfTime()
    {
        Assert.True(BroadcastTime.TryReadDuration([0x02, 0x40, 0x00], out var runs));

        Assert.Equal(new TimeSpan(2, 40, 0), runs);
    }

    [Fact]
    public void ADurationTheBroadcastLeavesOpenIsReadAsUnknownRatherThanRefused()
    {
        Assert.True(BroadcastTime.TryReadDuration([0xFF, 0xFF, 0xFF], out var runs));

        Assert.Null(runs);
    }

    [Fact]
    public void ADurationThatIsNotPackedDecimalIsRefused()
    {
        Assert.False(BroadcastTime.TryReadDuration([0x0A, 0x00, 0x00], out _));
    }
}
