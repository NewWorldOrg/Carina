using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class TunerStandingTests
{
    private static readonly QualityTally NothingRecorded = QualityAggregator.Tally([]);

    private static readonly QualityTally RecordedWell = QualityAggregator.Tally([QualityFactory.Measured(0.0001)]);

    private static readonly QualityTally RecordedBeyondWarning = QualityAggregator.Tally([QualityFactory.Measured(0.0005)]);

    private static readonly QualityTally RecordedUnwatchable = QualityAggregator.Tally([QualityFactory.Measured(0.005)]);

    private static readonly QualityTally RecordedUnmeasured = QualityAggregator.Tally([QualityFactory.Unmeasured()]);

    private static readonly QualityReading SignalGood = QualityReading.Of(1, 1, 0);

    private static readonly QualityReading SignalBeyond = QualityReading.Of(1, 1, 1);

    private static readonly QualityReading SignalUnmeasured = QualityReading.Of(1, 0, 0);

    private static readonly QualityReading SignalNotSupplied = QualityReading.NotSupplied(1, 0);

    private static readonly QualityReading SignalUnsupported = QualityReading.Unsupported();

    [Fact(DisplayName = "BR-QD-017: a bit error rate beyond its level shows on the row even when nothing was recorded")]
    public void ABitErrorRateBeyondItsLevelShowsOnTheRowEvenWhenNothingWasRecorded()
    {
        TunerStanding standing = TunerStandings.Of([NothingRecorded], [SignalGood, SignalGood, SignalBeyond], cannotLock: false);

        Assert.Equal(new TunerStanding(QualityStanding.Warning, CannotLock: false), standing);
    }

    [Fact(DisplayName = "BR-QD-017: a tuner that cannot lock stands as may not be watchable, and says why")]
    public void ATunerThatCannotLockStandsAsMayNotBeWatchableAndSaysWhy()
    {
        TunerStanding standing = TunerStandings.Of([NothingRecorded], [SignalUnmeasured, SignalUnmeasured, SignalUnmeasured], cannotLock: true);

        Assert.Equal(new TunerStanding(QualityStanding.MayNotBeWatchable, CannotLock: true), standing);
    }

    [Fact(DisplayName = "BR-QD-017: an enabled tuner nobody measured is unmeasured, not nothing to measure")]
    public void AnEnabledTunerNobodyMeasuredIsUnmeasured()
    {
        TunerStanding standing = TunerStandings.Of([NothingRecorded], [SignalUnmeasured, SignalUnmeasured, SignalUnmeasured], cannotLock: false);

        Assert.Equal(new TunerStanding(QualityStanding.Unmeasured, CannotLock: false), standing);
    }

    [Fact(DisplayName = "BR-QD-017: a tuner with nothing to say but readings it cannot take is unmeasured")]
    public void ATunerWithNothingToSayButReadingsItCannotTakeIsUnmeasured()
    {
        TunerStanding standing = TunerStandings.Of([NothingRecorded], [SignalUnsupported], cannotLock: false);

        Assert.Equal(QualityStanding.Unmeasured, standing.Standing);
    }

    [Fact(DisplayName = "BR-QD-017: the worst band among the recordings and the signal wins")]
    public void TheWorstBandAmongTheRecordingsAndTheSignalWins()
    {
        Assert.Equal(
            QualityStanding.MayNotBeWatchable,
            TunerStandings.Of([RecordedUnwatchable], [SignalBeyond], cannotLock: false).Standing);
        Assert.Equal(
            QualityStanding.Warning,
            TunerStandings.Of([RecordedBeyondWarning], [SignalGood], cannotLock: false).Standing);
    }

    [Fact(DisplayName = "BR-QD-017: a reading beyond its level outweighs a supply that went quiet, which outweighs what was measured well")]
    public void AReadingBeyondItsLevelOutweighsAQuietSupplyWhichOutweighsWhatWasMeasuredWell()
    {
        Assert.Equal(
            QualityStanding.Warning,
            TunerStandings.Of([RecordedUnmeasured], [SignalNotSupplied, SignalBeyond], cannotLock: false).Standing);
        Assert.Equal(
            QualityStanding.Unreachable,
            TunerStandings.Of([RecordedWell], [SignalNotSupplied, SignalGood], cannotLock: false).Standing);
    }

    [Fact(DisplayName = "BR-QD-017: a tuner is unmeasured only when nothing on it was measured")]
    public void ATunerIsUnmeasuredOnlyWhenNothingOnItWasMeasured()
    {
        Assert.Equal(
            QualityStanding.Good,
            TunerStandings.Of([RecordedUnmeasured], [SignalGood, SignalUnmeasured], cannotLock: false).Standing);
        Assert.Equal(
            QualityStanding.Good,
            TunerStandings.Of([RecordedWell], [SignalUnmeasured], cannotLock: false).Standing);
        Assert.Equal(
            QualityStanding.Unmeasured,
            TunerStandings.Of([RecordedUnmeasured], [SignalUnmeasured, SignalUnsupported], cannotLock: false).Standing);
    }

    [Fact(DisplayName = "BR-QD-017: nothing to measure and a reading it cannot take leave a healthy tuner healthy")]
    public void NothingToMeasureAndAReadingItCannotTakeLeaveAHealthyTunerHealthy()
    {
        TunerStanding standing = TunerStandings.Of([NothingRecorded], [SignalGood, SignalUnsupported], cannotLock: false);

        Assert.Equal(new TunerStanding(QualityStanding.Good, CannotLock: false), standing);
        Assert.Equal(
            QualityStanding.Good,
            TunerStandings.Of([RecordedWell], [SignalGood], cannotLock: false).Standing);
    }

    [Fact(DisplayName = "BR-QD-017: worst first puts a tuner that cannot lock on top and the ones nobody measured last")]
    public void WorstFirstPutsATunerThatCannotLockOnTopAndTheOnesNobodyMeasuredLast()
    {
        TunerStanding[] shuffled =
        [
            new(QualityStanding.Unmeasured, false),
            new(QualityStanding.Good, false),
            new(QualityStanding.Unreachable, false),
            new(QualityStanding.MayNotBeWatchable, true),
            new(QualityStanding.Warning, false),
            new(QualityStanding.MayNotBeWatchable, false),
        ];

        Assert.Equal(
            [
                new TunerStanding(QualityStanding.MayNotBeWatchable, true),
                new TunerStanding(QualityStanding.MayNotBeWatchable, false),
                new TunerStanding(QualityStanding.Warning, false),
                new TunerStanding(QualityStanding.Unreachable, false),
                new TunerStanding(QualityStanding.Good, false),
                new TunerStanding(QualityStanding.Unmeasured, false),
            ],
            shuffled.OrderByDescending(TunerStandings.Severity));
    }
}
