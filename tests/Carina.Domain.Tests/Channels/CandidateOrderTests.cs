using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class CandidateOrderTests
{
    private const int LowChannel = 41;
    private const int HighChannel = 58;

    private static readonly DateTime At = new(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheChannelThatMeasuredTheHigherCarrierToNoiseIsTheOneChosen()
    {
        CandidateChannel weak = Measured(HighChannel, SignalMeasurement.WithLock(At, 12_000));
        CandidateChannel strong = Measured(LowChannel, SignalMeasurement.WithLock(At, 29_000));

        Assert.Equal(strong, CandidateOrder.Best([weak, strong]));
    }

    [Fact]
    public void TheLowerChannelNumberDoesNotWinOverTheBetterMeasurement()
    {
        CandidateChannel low = Measured(LowChannel, SignalMeasurement.WithLock(At, 12_000));
        CandidateChannel high = Measured(HighChannel, SignalMeasurement.WithLock(At, 29_000));

        Assert.Equal(high, CandidateOrder.Best([low, high]));
    }

    [Fact]
    public void AChannelThatLockedComesAheadOfOneThatDidNot()
    {
        CandidateChannel unlocked = Measured(LowChannel, SignalMeasurement.WithoutLock(At));
        CandidateChannel locked = Measured(HighChannel, SignalMeasurement.WithLock(At, 8_000));

        Assert.Equal(locked, CandidateOrder.Best([unlocked, locked]));
    }

    [Fact]
    public void AChannelThatWasMeasuredComesAheadOfOneThatNeverWas()
    {
        CandidateChannel unmeasured = Measured(LowChannel, null);
        CandidateChannel measured = Measured(HighChannel, SignalMeasurement.WithLock(At, 8_000));

        Assert.Equal(measured, CandidateOrder.Best([unmeasured, measured]));
    }

    [Fact]
    public void AChannelWithNoReadingToShowDoesNotOutrankOneThatLockedWithoutACarrierToNoiseFigure()
    {
        CandidateChannel figureless = Measured(HighChannel, SignalMeasurement.WithLock(At));
        CandidateChannel unlocked = Measured(LowChannel, SignalMeasurement.WithoutLock(At));

        Assert.Equal(figureless, CandidateOrder.Best([unlocked, figureless]));
    }

    [Fact]
    public void NothingToTellTheChannelsApartLeavesTheOrderTheSameEveryTime()
    {
        CandidateChannel later = Measured(HighChannel, null);
        CandidateChannel earlier = Measured(LowChannel, null);

        Assert.Equal(earlier, CandidateOrder.Best([later, earlier]));
        Assert.Equal(earlier, CandidateOrder.Best([earlier, later]));
    }

    [Fact]
    public void NoChannelsAtAllMeansThereIsNothingToChoose()
        => Assert.Null(CandidateOrder.Best([]));

    private static CandidateChannel Measured(int physicalChannel, SignalMeasurement? measurement)
    {
        CandidateChannel candidate = CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(1),
            new ServiceId(101),
            TuningParameters.Terrestrial(physicalChannel),
            At);

        if (measurement is not null)
        {
            candidate.RecordTuningSuccess(measurement, At);
        }

        return candidate;
    }
}
