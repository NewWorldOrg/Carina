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

    [Fact]
    public void TheChannelThatMeasuredBetterThanTheSelectedOneIsNamed()
    {
        CandidateChannel selected = Chosen(Measured(HighChannel, SignalMeasurement.WithLock(At, 12_000)));
        CandidateChannel better = Measured(LowChannel, SignalMeasurement.WithLock(At, 29_000));

        Assert.Equal(better, CandidateOrder.BetterThanTheSelected([selected, better]));
    }

    [Fact]
    public void TheSelectedChannelIsNamedAgainstNothingWhenTheMeasurementsAlreadyFavourIt()
    {
        CandidateChannel selected = Chosen(Measured(LowChannel, SignalMeasurement.WithLock(At, 29_000)));
        CandidateChannel weaker = Measured(HighChannel, SignalMeasurement.WithLock(At, 12_000));

        Assert.Null(CandidateOrder.BetterThanTheSelected([selected, weaker]));
    }

    [Fact]
    public void AChannelChosenByHandIsWeighedTheSameWayAsOneAScanChose()
    {
        CandidateChannel byHand = Chosen(
            Measured(HighChannel, SignalMeasurement.WithLock(At, 12_000)),
            SelectionSource.Manual);
        CandidateChannel better = Measured(LowChannel, SignalMeasurement.WithLock(At, 29_000));

        Assert.Equal(better, CandidateOrder.BetterThanTheSelected([byHand, better]));
    }

    [Fact]
    public void AServiceWithNothingSelectedHasNothingToBeOutranked()
    {
        CandidateChannel weak = Measured(HighChannel, SignalMeasurement.WithLock(At, 12_000));
        CandidateChannel strong = Measured(LowChannel, SignalMeasurement.WithLock(At, 29_000));

        Assert.Null(CandidateOrder.BetterThanTheSelected([weak, strong]));
    }

    [Fact]
    public void AChannelTheMeasurementsCannotSeparateFromTheSelectedOneDoesNotOutrankIt()
    {
        CandidateChannel selected = Chosen(Measured(HighChannel, SignalMeasurement.WithLock(At, 29_000)));
        CandidateChannel even = Measured(LowChannel, SignalMeasurement.WithLock(At, 29_000));

        Assert.Null(CandidateOrder.BetterThanTheSelected([selected, even]));
    }

    [Fact]
    public void AChannelNobodyHasMeasuredDoesNotOutrankASelectedOneNobodyHasMeasuredEither()
    {
        CandidateChannel selected = Chosen(Measured(HighChannel, null));
        CandidateChannel unmeasured = Measured(LowChannel, null);

        Assert.Null(CandidateOrder.BetterThanTheSelected([selected, unmeasured]));
    }

    [Fact]
    public void ReadingWhetherTheSelectionIsOutrankedLeavesTheSelectionWhereItWas()
    {
        CandidateChannel selected = Chosen(Measured(HighChannel, SignalMeasurement.WithLock(At, 12_000)));
        CandidateChannel better = Measured(LowChannel, SignalMeasurement.WithLock(At, 29_000));

        CandidateOrder.BetterThanTheSelected([selected, better]);

        Assert.True(selected.IsSelected);
        Assert.False(better.IsSelected);
    }

    private static CandidateChannel Chosen(
        CandidateChannel candidate,
        SelectionSource source = SelectionSource.Scan)
    {
        candidate.Select(source, candidate.LastMeasurement, At);

        return candidate;
    }

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
