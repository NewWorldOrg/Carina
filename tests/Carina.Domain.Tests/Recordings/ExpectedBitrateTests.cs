using Carina.Contracts;

using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class ExpectedBitrateTests
{
    [Fact]
    public void AStreamWeighsWhatItCarriedForAsLongAsItRan()
    {
        var bitrate = new ExpectedBitrate(16_000_000, 24_000_000);

        Assert.Equal(2_000_000_000L, bitrate.LeastBytesOver(TimeSpan.FromSeconds(1000)));
        Assert.Equal(3_000_000_000L, bitrate.MostBytesOver(TimeSpan.FromSeconds(1000)));
    }

    [Fact]
    public void HalfASecondWeighsHalfAsMuchAsASecond()
    {
        var bitrate = new ExpectedBitrate(16_000_000, 24_000_000);

        Assert.Equal(1_000_000L, bitrate.LeastBytesOver(TimeSpan.FromSeconds(0.5)));
        Assert.Equal(1_500_000L, bitrate.MostBytesOver(TimeSpan.FromSeconds(0.5)));
    }

    [Fact]
    public void NoTimeAtAllWeighsNothing()
    {
        var bitrate = new ExpectedBitrate(16_000_000, 24_000_000);

        Assert.Equal(0L, bitrate.LeastBytesOver(TimeSpan.Zero));
        Assert.Equal(0L, bitrate.MostBytesOver(TimeSpan.Zero));
    }

    [Fact]
    public void APartOfAByteIsNotAByte()
    {
        var bitrate = new ExpectedBitrate(9, 15);

        Assert.Equal(1L, bitrate.LeastBytesOver(TimeSpan.FromSeconds(1)));
        Assert.Equal(1L, bitrate.MostBytesOver(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ATerrestrialHourWeighsBetweenSixAndSevenGigabytes()
    {
        var bitrate = new ExpectedBitrate(14_300_000, 16_500_000);

        Assert.Equal(6_435_000_000L, bitrate.LeastBytesOver(TimeSpan.FromHours(1)));
        Assert.Equal(7_425_000_000L, bitrate.MostBytesOver(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void AStreamThatRanBackwardsIsRefused()
    {
        var bitrate = new ExpectedBitrate(16_000_000, 24_000_000);

        ArgumentOutOfRangeException refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => bitrate.LeastBytesOver(TimeSpan.FromTicks(-1)));

        Assert.Equal("span", refusal.ParamName);
    }

    [Fact]
    public void AStreamThatRanBackwardsIsRefusedAtTheTopOfTheRangeToo()
    {
        var bitrate = new ExpectedBitrate(16_000_000, 24_000_000);

        ArgumentOutOfRangeException refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => bitrate.MostBytesOver(TimeSpan.FromTicks(-1)));

        Assert.Equal("span", refusal.ParamName);
    }

    [Fact]
    public void ABroadcastThatCarriesNothingIsRefused()
    {
        ArgumentOutOfRangeException refusal =
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExpectedBitrate(0, 24_000_000));

        Assert.Equal("leastBitsPerSecond", refusal.ParamName);
    }

    [Fact]
    public void ARangeThatReachesDownwardsIsRefused()
    {
        ArgumentOutOfRangeException refusal =
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExpectedBitrate(24_000_000, 23_999_999));

        Assert.Equal("mostBitsPerSecond", refusal.ParamName);
    }

    [Fact]
    public void ARangeThatIsASinglePointIsRefused()
    {
        ArgumentOutOfRangeException refusal =
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExpectedBitrate(16_000_000, 16_000_000));

        Assert.Equal("mostBitsPerSecond", refusal.ParamName);
    }

    [Fact]
    public void ARangeOneBitWideIsAllowed()
    {
        var bitrate = new ExpectedBitrate(16_000_000, 16_000_001);

        Assert.Equal(16_000_001L, bitrate.MostBitsPerSecond);
    }

    [Fact]
    public void TheRateMeasuredOffTerrestrialBroadcastsIsTheOneTheLedgerCarries()
    {
        Assert.Equal(14_300_000L, ExpectedBitrate.Terrestrial.LeastBitsPerSecond);
        Assert.Equal(16_500_000L, ExpectedBitrate.Terrestrial.MostBitsPerSecond);
    }

    [Fact]
    public void TheRateMeasuredOffSatelliteBroadcastsIsTheOneTheLedgerCarries()
    {
        Assert.Equal(11_100_000L, ExpectedBitrate.Satellite.LeastBitsPerSecond);
        Assert.Equal(12_200_000L, ExpectedBitrate.Satellite.MostBitsPerSecond);
    }

    [Fact]
    public void ATerrestrialTunerIsWeighedAgainstTheTerrestrialRate()
        => Assert.Equal(ExpectedBitrate.Terrestrial, ExpectedBitrate.Of(TunerKind.Terrestrial));

    [Fact]
    public void ASatelliteTunerIsWeighedAgainstTheSatelliteRate()
        => Assert.Equal(ExpectedBitrate.Satellite, ExpectedBitrate.Of(TunerKind.Satellite));

    [Fact]
    public void ATunerOfNoNamedKindHasNoMeasuredRate()
    {
        ArgumentOutOfRangeException refusal =
            Assert.Throws<ArgumentOutOfRangeException>(() => ExpectedBitrate.Of(TunerKind.Unspecified));

        Assert.Equal("kind", refusal.ParamName);
    }

    [Fact]
    public void TheTerrestrialRangeIsWiderThanTheSlackItIsJudgedWith()
        => Assert.True(ExpectedBitrate.Terrestrial.MostBitsPerSecond * 100
            > ExpectedBitrate.Terrestrial.LeastBitsPerSecond * (100 + Slack));

    [Fact]
    public void TheSatelliteRangeIsNarrowerThanTheSlackItIsJudgedWithAndNobodyHasDecidedWhatToDoAboutIt()
        => Assert.True(ExpectedBitrate.Satellite.MostBitsPerSecond * 100
            < ExpectedBitrate.Satellite.LeastBitsPerSecond * (100 + Slack));

    [Fact]
    public void AStreamCanWeighMoreThanALongCanHold()
    {
        var bitrate = new ExpectedBitrate(1_000_000_000, 2_000_000_000);

        Assert.True(bitrate.LeastBytesOver(TimeSpan.MaxValue) > (Int128)long.MaxValue);
        Assert.True(bitrate.MostBytesOver(TimeSpan.MaxValue) > bitrate.LeastBytesOver(TimeSpan.MaxValue));
    }

    [Fact]
    public void AStreamThatWeighsMoreThanALongIsStillWeighedTheRightWayRound()
    {
        var bitrate = new ExpectedBitrate(1_000_000_000, 2_000_000_000);

        Assert.True(bitrate.LeastBytesOver(TimeSpan.MaxValue) > Int128.Zero);
        Assert.True(bitrate.MostBytesOver(TimeSpan.MaxValue) > Int128.Zero);
    }

    private static int Slack => CompletionTolerance.Default.SizeSlackPercent;
}
